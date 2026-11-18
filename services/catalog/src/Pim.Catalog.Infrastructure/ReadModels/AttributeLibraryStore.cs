using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.AttributeLibrary;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Attribute Library's read model.
///
/// Its one interesting decision is the coverage DENOMINATOR. An attribute is
/// only expected on products whose attribute set includes it, so coverage is
/// measured against those products and not against the catalogue. Measured
/// against the catalogue, a correctly scoped attribute used by one product
/// family reads as 3% — abandoned — and the number would punish precisely the
/// modelling it is supposed to encourage.
///
/// The "filled" figure applies the same rule as the grid, the cockpit and the
/// category screen: approved values, base locale or language-neutral. Four
/// screens, one definition of "this is filled in".
///
/// R-17 applies: every SELECT sits above the positional record it fills.
/// </summary>
public sealed class AttributeLibraryStore(CatalogDbContext dbContext) : IAttributeLibraryStore
{
    private readonly CatalogDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <summary>
    /// Per-attribute usage: how many products it applies to, and how many of
    /// those actually carry an approved value.
    /// </summary>
    private const string UsageCte = """
        WITH base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1),
        applies AS (
            SELECT sa.attribute_id, count(p.id)::int AS n
            FROM catalog.attribute_set_attribute sa
            JOIN catalog.product p ON p.attribute_set_id = sa.set_id
            GROUP BY sa.attribute_id
        ),
        filled AS (
            SELECT av.attribute_id, count(DISTINCT av.product_id)::int AS n
            FROM catalog.attribute_value av
            JOIN catalog.product p ON p.id = av.product_id
            JOIN catalog.attribute_set_attribute sa
                 ON sa.set_id = p.attribute_set_id AND sa.attribute_id = av.attribute_id
            WHERE av.data_state = 'approved'
              AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
            GROUP BY av.attribute_id
        )
        """;

    public async Task<AttributeLibraryDto> GetLibraryAsync(
        DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var parameters = new { displayLocale = locale.Code };

        var attributes = await ReadAttributesAsync(connection, parameters, cancellationToken);
        var groups = await ReadGroupsAsync(connection, parameters, cancellationToken);

        return new AttributeLibraryDto(groups, attributes, attributes.Count);
    }

    // Column order matches SummaryRow exactly. R-17.
    private static async Task<IReadOnlyList<AttributeSummaryDto>> ReadAttributesAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = UsageCte + """
            ,
            opts AS (
                SELECT attribute_id, count(*)::int AS n
                FROM catalog.attribute_option GROUP BY attribute_id
            ),
            sets AS (
                SELECT attribute_id, count(*)::int AS n, bool_or(is_required) AS required_anywhere
                FROM catalog.attribute_set_attribute GROUP BY attribute_id
            )
            SELECT a.code,
                   COALESCE(atr.label, a.code)          AS label,
                   atr.locale_code                      AS label_locale,
                   COALESCE(g.code, '')                 AS group_code,
                   COALESCE(gtr.name, g.code, '')       AS group_label,
                   a.data_type,
                   u.code                               AS unit,
                   a.is_localizable,
                   a.is_variant_axis,
                   a.scope,
                   a.source_authority,
                   COALESCE(o.n, 0)::int                AS option_count,
                   COALESCE(s.n, 0)::int                AS set_count,
                   COALESCE(s.required_anywhere, false) AS required_in_any_set,
                   COALESCE(ap.n, 0)::int               AS applies_to_products,
                   COALESCE(f.n, 0)::int                AS products_with_value,
                   CASE WHEN COALESCE(ap.n, 0) = 0 THEN 0
                        ELSE round(100.0 * COALESCE(f.n, 0) / ap.n)
                   END::int                             AS coverage_pct
            FROM catalog.attribute a
            LEFT JOIN catalog.attribute_group g ON g.id = a.group_id
            LEFT JOIN catalog.unit u            ON u.id = a.unit_id
            LEFT JOIN opts o    ON o.attribute_id = a.id
            LEFT JOIN sets s    ON s.attribute_id = a.id
            LEFT JOIN applies ap ON ap.attribute_id = a.id
            LEFT JOIN filled f  ON f.attribute_id = a.id
            LEFT JOIN LATERAL (
                SELECT atr2.label, atr2.locale_code
                FROM catalog.attribute_tr atr2
                WHERE atr2.attribute_id = a.id
                  AND atr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (atr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) atr ON true
            LEFT JOIN LATERAL (
                SELECT gtr2.name
                FROM catalog.attribute_group_tr gtr2
                WHERE gtr2.group_id = g.id
                  AND gtr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (gtr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) gtr ON true
            ORDER BY g.sort_order NULLS LAST, a.code
            """;

        var rows = await DapperGateway.QueryOnAsync<SummaryRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeSummaryDto(
            r.Code, r.Label, r.LabelLocale, r.GroupCode, r.GroupLabel, r.DataType, r.Unit,
            r.IsLocalizable, r.IsVariantAxis, r.Scope, r.SourceAuthority,
            r.OptionCount, r.SetCount, r.RequiredInAnySet,
            r.AppliesToProducts, r.ProductsWithValue, r.CoveragePct))];
    }

    private sealed record SummaryRow(
        string Code,
        string Label,
        string? LabelLocale,
        string GroupCode,
        string GroupLabel,
        string DataType,
        string? Unit,
        bool IsLocalizable,
        bool IsVariantAxis,
        string Scope,
        string SourceAuthority,
        int OptionCount,
        int SetCount,
        bool RequiredInAnySet,
        int AppliesToProducts,
        int ProductsWithValue,
        int CoveragePct);

    /// <summary>
    /// The groups, including any that currently hold no attributes.
    ///
    /// Derived from attribute_group rather than from the attributes on screen:
    /// a group with nothing in it is a real and interesting state (somebody
    /// created it and never used it), and inferring the list from the rows would
    /// make it invisible. Column order matches GroupRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeGroupSummaryDto>> ReadGroupsAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1)
            SELECT g.code,
                   COALESCE(gtr.name, g.code) AS label,
                   gtr.locale_code            AS label_locale,
                   count(a.id)::int           AS attribute_count
            FROM catalog.attribute_group g
            LEFT JOIN catalog.attribute a ON a.group_id = g.id
            LEFT JOIN LATERAL (
                SELECT gtr2.name, gtr2.locale_code
                FROM catalog.attribute_group_tr gtr2
                WHERE gtr2.group_id = g.id
                  AND gtr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (gtr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) gtr ON true
            GROUP BY g.code, g.sort_order, gtr.name, gtr.locale_code
            ORDER BY g.sort_order, g.code
            """;

        var rows = await DapperGateway.QueryOnAsync<GroupRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeGroupSummaryDto(
            r.Code, r.Label, r.LabelLocale, r.AttributeCount))];
    }

    private sealed record GroupRow(string Code, string Label, string? LabelLocale, int AttributeCount);

    public async Task<AttributeDetailDto?> GetDetailAsync(
        string code, DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var parameters = new { code, displayLocale = locale.Code };

        var head = await ReadDetailHeadAsync(connection, parameters, cancellationToken);
        if (head is null)
        {
            return null;
        }

        var labels = await ReadLabelsAsync(connection, parameters, cancellationToken);
        var options = await ReadOptionsAsync(connection, parameters, cancellationToken);
        var sets = await ReadSetsAsync(connection, parameters, cancellationToken);

        return new AttributeDetailDto(
            head.Code, head.Label, head.LabelLocale, head.GroupCode, head.GroupLabel,
            head.DataType, head.UnitName, head.UnitCode, head.IsLocalizable, head.IsVariantAxis,
            head.Scope, head.SourceAuthority, head.ValidationJson,
            head.AppliesToProducts, head.ProductsWithValue, head.CoveragePct,
            labels, options, sets);
    }

    // Column order matches DetailHeadRow exactly. R-17.
    private static async Task<DetailHeadRow?> ReadDetailHeadAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = UsageCte + """

            SELECT a.code,
                   COALESCE(atr.label, a.code)    AS label,
                   atr.locale_code                AS label_locale,
                   COALESCE(g.code, '')           AS group_code,
                   COALESCE(gtr.name, g.code, '') AS group_label,
                   a.data_type,
                   utr.name                       AS unit_name,
                   u.code                         AS unit_code,
                   a.is_localizable,
                   a.is_variant_axis,
                   a.scope,
                   a.source_authority,
                   a.validation_json::text        AS validation_json,
                   COALESCE(ap.n, 0)::int         AS applies_to_products,
                   COALESCE(f.n, 0)::int          AS products_with_value,
                   CASE WHEN COALESCE(ap.n, 0) = 0 THEN 0
                        ELSE round(100.0 * COALESCE(f.n, 0) / ap.n)
                   END::int                       AS coverage_pct
            FROM catalog.attribute a
            LEFT JOIN catalog.attribute_group g ON g.id = a.group_id
            LEFT JOIN catalog.unit u            ON u.id = a.unit_id
            LEFT JOIN applies ap ON ap.attribute_id = a.id
            LEFT JOIN filled f   ON f.attribute_id = a.id
            LEFT JOIN LATERAL (
                SELECT atr2.label, atr2.locale_code
                FROM catalog.attribute_tr atr2
                WHERE atr2.attribute_id = a.id
                  AND atr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (atr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) atr ON true
            LEFT JOIN LATERAL (
                SELECT gtr2.name
                FROM catalog.attribute_group_tr gtr2
                WHERE gtr2.group_id = g.id
                  AND gtr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (gtr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) gtr ON true
            LEFT JOIN LATERAL (
                SELECT utr2.name
                FROM catalog.unit_tr utr2
                WHERE utr2.unit_id = u.id
                  AND utr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (utr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) utr ON true
            WHERE a.code = @code
            """;

        return await DapperGateway.QuerySingleOrDefaultOnAsync<DetailHeadRow>(
            connection, sql, parameters, cancellationToken);
    }

    private sealed record DetailHeadRow(
        string Code,
        string Label,
        string? LabelLocale,
        string GroupCode,
        string GroupLabel,
        string DataType,
        string? UnitName,
        string? UnitCode,
        bool IsLocalizable,
        bool IsVariantAxis,
        string Scope,
        string SourceAuthority,
        string? ValidationJson,
        int AppliesToProducts,
        int ProductsWithValue,
        int CoveragePct);

    /// <summary>
    /// One row per configured locale, translated or not.
    ///
    /// The untranslated rows are the point: this panel is where somebody finds
    /// out that an attribute has no French label, and an inner join would hide
    /// exactly that. Column order matches LabelRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeLabelDto>> ReadLabelsAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.code AS locale_code,
                   l.name AS locale_name,
                   l.is_base,
                   tr.label,
                   tr.help_text,
                   tr.placeholder
            FROM catalog.locale l
            LEFT JOIN catalog.attribute_tr tr
                   ON tr.locale_code = l.code
                  AND tr.attribute_id = (SELECT id FROM catalog.attribute WHERE code = @code)
            WHERE l.is_active
            ORDER BY l.is_base DESC, l.code
            """;

        var rows = await DapperGateway.QueryOnAsync<LabelRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeLabelDto(
            r.LocaleCode, r.LocaleName, r.IsBase, r.Label, r.HelpText, r.Placeholder))];
    }

    private sealed record LabelRow(
        string LocaleCode,
        string LocaleName,
        bool IsBase,
        string? Label,
        string? HelpText,
        string? Placeholder);

    /// <summary>
    /// The attribute's options, each with how many products actually chose it.
    ///
    /// The usage count turns a list of options into something you can act on: an
    /// option nobody has ever selected is either dead vocabulary or a data-entry
    /// gap, and neither is visible from the list alone. Column order matches
    /// OptionRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeOptionDetailDto>> ReadOptionsAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1),
            attr AS (SELECT id FROM catalog.attribute WHERE code = @code)
            SELECT o.code,
                   COALESCE(otr.label, o.code) AS label,
                   otr.locale_code             AS label_locale,
                   o.sort_order,
                   (SELECT count(DISTINCT av.product_id)::int
                    FROM catalog.attribute_value av
                    WHERE av.attribute_id = (SELECT id FROM attr)
                      AND av.data_state = 'approved'
                      AND av.value_option_id = o.id) AS used_by_products
            FROM catalog.attribute_option o
            JOIN attr ON o.attribute_id = attr.id
            LEFT JOIN LATERAL (
                SELECT otr2.label, otr2.locale_code
                FROM catalog.attribute_option_tr otr2
                WHERE otr2.option_id = o.id
                  AND otr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (otr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) otr ON true
            ORDER BY o.sort_order, o.code
            """;

        var rows = await DapperGateway.QueryOnAsync<OptionRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeOptionDetailDto(
            r.Code, r.Label, r.LabelLocale, r.SortOrder, r.UsedByProducts))];
    }

    private sealed record OptionRow(
        string Code,
        string Label,
        string? LabelLocale,
        int SortOrder,
        int UsedByProducts);

    /// <summary>
    /// Which sets include this attribute, on what terms, and how many products
    /// each set covers. Column order matches SetRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeSetMembershipDto>> ReadSetsAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.code AS set_code,
                   s.name AS set_name,
                   sa.is_required,
                   sa.sort_order,
                   (SELECT count(*)::int FROM catalog.product p WHERE p.attribute_set_id = s.id)
                       AS product_count
            FROM catalog.attribute_set_attribute sa
            JOIN catalog.attribute_set s ON s.id = sa.set_id
            JOIN catalog.attribute a     ON a.id = sa.attribute_id
            WHERE a.code = @code
            ORDER BY s.code
            """;

        var rows = await DapperGateway.QueryOnAsync<SetRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeSetMembershipDto(
            r.SetCode, r.SetName, r.IsRequired, r.SortOrder, r.ProductCount))];
    }

    private sealed record SetRow(
        string SetCode,
        string SetName,
        bool IsRequired,
        int SortOrder,
        int ProductCount);
}
