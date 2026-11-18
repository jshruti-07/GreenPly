using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Domain.Packaging;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Where a product may be used, as a set assignment over reference data.
///
/// The whole set is replaced on save. Deltas would need the client to track
/// which tags it added and removed since load, and two stewards saving
/// concurrently could interleave into a set neither of them chose — the
/// rowVersion check makes the second one a 409 instead.
/// </summary>
public sealed class ProductApplicationStore(CatalogDbContext dbContext) : IProductApplicationStore
{
    /// <summary>Display order and labels for the four groups.</summary>
    private static readonly (string Code, string Label)[] Groups =
    [
        ("INDUSTRY", "Industries"),
        ("BUILDING_TYPE", "Building types"),
        ("AREA", "Building areas"),
        ("USE_CASE", "Use cases"),
    ];

    public Task<ProductApplicationsDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, productCode, locale, cancellationToken),
            cancellationToken);

    public Task<Result<ProductApplicationsDto>> SetAsync(
        SetProductApplicationsCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var known = await LoadApplicationIdsAsync(
                    scope.Connection, scope.Transaction, cancellationToken);

                // Validate the whole set before touching anything: a single
                // unknown code must not leave a half-applied assignment.
                var ids = new List<long>(command.ApplicationCodes.Count);
                foreach (var code in command.ApplicationCodes)
                {
                    if (!known.TryGetValue(code, out var id))
                    {
                        return ApplicationErrors.UnknownApplication(code);
                    }

                    ids.Add(id);
                }

                const string deleteSql =
                    "DELETE FROM catalog.product_application WHERE product_id = @productId";

                await using (var delete = new NpgsqlCommand(deleteSql, scope.Connection, scope.Transaction))
                {
                    delete.Parameters.AddWithValue("productId", scope.ProductId);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }

                if (ids.Count == 0)
                {
                    return null;
                }

                // unnest so the whole set is one round trip regardless of size.
                const string insertSql = """
                    INSERT INTO catalog.product_application (product_id, application_id)
                    SELECT @productId, unnest(@ids::bigint[])
                    ON CONFLICT DO NOTHING
                    """;

                await using var insert = new NpgsqlCommand(insertSql, scope.Connection, scope.Transaction);
                insert.Parameters.AddWithValue("productId", scope.ProductId);
                insert.Parameters.AddWithValue("ids", ids.ToArray());
                await insert.ExecuteNonQueryAsync(cancellationToken);

                return null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    private static async Task<Dictionary<string, long>> LoadApplicationIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT code, id FROM catalog.application";

        var rows = await DapperGateway.QueryOnAsync<ApplicationIdRow>(
            connection, sql, parameters: null, cancellationToken, transaction);

        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[row.Code] = row.Id;
        }

        return map;
    }

    private static async Task<ProductApplicationsDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        const string productSql = """
            SELECT id, row_version, base_locale FROM catalog.product WHERE product_code = @code
            """;

        var header = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductHeaderRow>(
            connection, productSql, new { code = productCode }, cancellationToken);

        if (header is null)
        {
            return null;
        }

        var (productId, rowVersion, baseLocale) = (header.Id, header.RowVersion, header.BaseLocale);

        // Every option is returned with its assigned flag, because the tab is a
        // picker: it has to show what is available, not only what is chosen.
        const string sql = """
            SELECT a.group_code, a.code,
                   COALESCE(tr_d.label, tr_b.label, a.code) AS label,
                   (pa.product_id IS NOT NULL)              AS is_assigned
            FROM catalog.application a
            LEFT JOIN catalog.application_tr tr_d
                   ON tr_d.application_id = a.id AND tr_d.locale_code = @displayLocale
            LEFT JOIN catalog.application_tr tr_b
                   ON tr_b.application_id = a.id AND tr_b.locale_code = @baseLocale
            LEFT JOIN catalog.product_application pa
                   ON pa.application_id = a.id AND pa.product_id = @productId
            ORDER BY a.group_code, a.sort_order, a.code
            """;

        // These are LABELS on reference data — the names of use cases a steward
        // picks from — so they follow the reader (ADR-009). Falling back to the
        // product's base locale before the code keeps a partly-translated
        // application list readable instead of showing raw codes.
        var optionRows = await DapperGateway.QueryOnAsync<ApplicationOptionRow>(
            connection,
            sql,
            new { productId, displayLocale = locale.Code, baseLocale },
            cancellationToken);

        var byGroup = new Dictionary<string, List<ApplicationOptionDto>>(StringComparer.Ordinal);
        foreach (var row in optionRows)
        {
            if (!byGroup.TryGetValue(row.GroupCode, out var options))
            {
                options = [];
                byGroup[row.GroupCode] = options;
            }

            options.Add(new ApplicationOptionDto(row.Code, row.Label, row.IsAssigned));
        }

        var groups = Groups
            .Select(g => new ApplicationGroupDto(
                g.Code,
                g.Label,
                byGroup.TryGetValue(g.Code, out var options) ? options : []))
            .ToList();

        return new ProductApplicationsDto(productCode, rowVersion, groups);
    }

    private sealed record ApplicationIdRow(string Code, long Id);

    private sealed record ProductHeaderRow(long Id, int RowVersion, string BaseLocale);

    private sealed record ApplicationOptionRow(
        string GroupCode, string Code, string Label, bool IsAssigned);
}
