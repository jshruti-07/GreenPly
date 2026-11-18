using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.Planning;
using Pim.Catalog.Domain.Assets;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Planning &amp; BIM tab, composed from three modules that already exist:
/// the 'planning' attribute group, the BIM classification schemes, and assets
/// whose type is BIM or CAD.
///
/// Read-only. Every value on this tab is written somewhere else — parameters on
/// Attributes, class assignments on Classifications, files on Media &amp;
/// Documents. A write path here would be a fourth way to modify the same three
/// tables, each needing its own concurrency handling.
/// </summary>
internal sealed class ProductPlanningStore(CatalogDbContext dbContext) : IProductPlanningStore
{
    /// <summary>
    /// The attribute group holding planning parameters, and the classification
    /// schemes a BIM tool consumes. Both are seed vocabulary rather than
    /// schema, so they are named once here instead of being spread through the
    /// SQL below.
    /// </summary>
    private const string PlanningGroupCode = "planning";

    private static readonly string[] BimSchemes = ["IFC", "OMNICLASS", "UNICLASS"];

    /// <summary>The parameter that answers "is there a BIM object for this?".</summary>
    private const string BimReadyAttribute = "bim_ready";

    public Task<ProductPlanningDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => LoadAsync(connection, productCode, locale, cancellationToken),
            cancellationToken);

    private static async Task<ProductPlanningDto?> LoadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        var (productId, rowVersion) =
            await ProductWriteScope.ResolveProductAsync(connection, productCode, cancellationToken);

        if (productId is null)
        {
            return null;
        }

        var parameters = await LoadParametersAsync(connection, productId.Value, locale, cancellationToken);
        var references = await LoadReferencesAsync(connection, productId.Value, cancellationToken);
        var geometry = await LoadGeometryAsync(connection, productId.Value, locale, cancellationToken);

        var bimReady = parameters
            .FirstOrDefault(p => string.Equals(p.Code, BimReadyAttribute, StringComparison.Ordinal))
            ?.Value switch
        {
            "true" => true,
            "false" => false,
            _ => (bool?)null,
        };

        return new ProductPlanningDto(
            productCode,
            rowVersion,
            bimReady,
            parameters.Count,
            parameters.Count(p => !string.IsNullOrEmpty(p.Value)),
            parameters,
            references,
            geometry);
    }

    /// <summary>
    /// Planning parameters. Value rendering matches the Attributes tab exactly
    /// — the same whole-number and fraction rules — because the two tabs show
    /// the same rows and a planner comparing them must not see 15 here and
    /// 15.0 there.
    /// </summary>
    private static async Task<List<PlanningParameterDto>> LoadParametersAsync(
        NpgsqlConnection connection,
        long productId,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.code,
                   COALESCE(atr_d.label, atr_b.label, a.code) AS label,
                   u.symbol,
                   a.data_type,
                   CASE
                     WHEN av.id IS NULL THEN NULL
                     WHEN a.data_type = 'list' THEN ao.code
                     WHEN a.data_type = 'boolean' THEN
                       CASE WHEN av.value_boolean THEN 'true' ELSE 'false' END
                     WHEN a.data_type IN ('number','decimal','measure') THEN
                       CASE
                         WHEN av.value_number = trunc(av.value_number)
                           THEN trunc(av.value_number)::bigint::text
                         ELSE rtrim(rtrim(av.value_number::text, '0'), '.')
                       END
                     WHEN a.data_type = 'date' THEN to_char(av.value_date, 'YYYY-MM-DD')
                     ELSE av.value_text
                   END AS value
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id
            JOIN catalog.attribute a ON a.id = sa.attribute_id
            JOIN catalog.attribute_group ag ON ag.id = a.group_id
                 AND (ag.code = @groupCode OR ag.code IN ('planning_bim', 'plywood_mechanical', 'plywood_dimensions') OR a.code = 'bim_ready')
            LEFT JOIN catalog.attribute_tr atr_d
                   ON atr_d.attribute_id = a.id AND atr_d.locale_code = @displayLocale
            LEFT JOIN catalog.attribute_tr atr_b
                   ON atr_b.attribute_id = a.id AND atr_b.locale_code = p.base_locale
            LEFT JOIN catalog.unit u ON u.id = a.unit_id
            LEFT JOIN catalog.attribute_value av
                   ON av.product_id = p.id AND av.attribute_id = a.id
            LEFT JOIN catalog.attribute_option ao ON ao.id = av.value_option_id
            WHERE p.id = @productId
            ORDER BY sa.sort_order, a.code
            """;

        // Parameter LABELS follow the reader; the values beneath them do not
        // move, because a measurement is not a translation. Same split as the
        // Attributes tab, and this tab is read-only anyway — every panel links
        // to the tab that owns the write.
        var rows = await DapperGateway.QueryOnAsync<ParameterRow>(
            connection,
            sql,
            new { productId, groupCode = PlanningGroupCode, displayLocale = locale.Code },
            cancellationToken);

        return rows
            .Select(r => new PlanningParameterDto(r.Code, r.Label, r.Value, r.Symbol, r.DataType))
            .ToList();
    }

    /// <summary>
    /// Every BIM scheme is returned, assigned or not, so the tab shows what a
    /// model author still needs rather than only what is already filled.
    /// </summary>
    private static async Task<List<BimReferenceDto>> LoadReferencesAsync(
        NpgsqlConnection connection,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.code    AS scheme_code,
                   s.name    AS scheme_name,
                   s.version AS scheme_version,
                   c.code    AS class_code,
                   c.label   AS class_label
            FROM catalog.classification_scheme s
            LEFT JOIN catalog.product_classification pc
                   ON pc.scheme_id = s.id AND pc.product_id = @productId
            LEFT JOIN catalog.classification_class c ON c.id = pc.class_id
            WHERE s.code = ANY(@schemes)
            ORDER BY s.sort_order, s.code
            """;

        var rows = await DapperGateway.QueryOnAsync<BimReferenceDto>(
            connection, sql, new { productId, schemes = BimSchemes }, cancellationToken);

        return rows.ToList();
    }

    /// <summary>
    /// Geometry files are assets, filtered by type rather than by role: a BIM
    /// object linked as DRAWING is still geometry, and a planner wants it.
    /// </summary>
    private static async Task<List<GeometryFileDto>> LoadGeometryAsync(
        NpgsqlConnection connection,
        long productId,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT al.id AS link_id, al.role, a.asset_type, a.file_name, a.file_size,
                   COALESCE(r.uri, a.storage_key) AS url,
                   COALESCE(atr_d.title, atr_b.title) AS title,
                   a.storage_provider
            FROM catalog.asset_link al
            JOIN catalog.asset a ON a.id = al.asset_id
            JOIN catalog.product p ON p.id = al.product_id
            LEFT JOIN catalog.asset_rendition r ON r.asset_id = a.id AND r.role = 'web'
            -- Follows the reader as of Sprint 19. It did not in Sprint 18, and
            -- the comment here said why: the Media tab could only write base
            -- locale, so a title shown in French here would have been one that
            -- tab could not edit. R-18 closed that -- asset titles are now read
            -- AND written per locale -- so the two tabs agree again.
            LEFT JOIN catalog.asset_tr atr_d
                   ON atr_d.asset_id = a.id AND atr_d.locale_code = @displayLocale
            LEFT JOIN catalog.asset_tr atr_b
                   ON atr_b.asset_id = a.id AND atr_b.locale_code = p.base_locale
            WHERE al.product_id = @productId
              AND a.asset_type = ANY(@types)
            ORDER BY al.sort_order, al.id
            """;

        var rows = await DapperGateway.QueryOnAsync<GeometryRow>(
            connection,
            sql,
            new { productId, types = new[] { AssetType.Bim, AssetType.Cad }, displayLocale = locale.Code },
            cancellationToken);

        return rows
            .Select(r => new GeometryFileDto(
                r.LinkId,
                r.Role,
                r.AssetType,
                r.FileName,
                r.FileSize,
                r.Url,
                r.Title,
                !string.Equals(r.StorageProvider, "local", StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// A planning parameter as stored. <c>Symbol</c> is the unit's symbol
    /// column; the DTO calls it UnitSymbol, so the two are mapped explicitly
    /// rather than aliased into a name that reads oddly in SQL.
    /// </summary>
    private sealed record ParameterRow(
        string Code, string Label, string? Symbol, string DataType, string? Value);

    private sealed record GeometryRow(
        long LinkId,
        string Role,
        string AssetType,
        string? FileName,
        long? FileSize,
        string Url,
        string? Title,
        string StorageProvider);
}
