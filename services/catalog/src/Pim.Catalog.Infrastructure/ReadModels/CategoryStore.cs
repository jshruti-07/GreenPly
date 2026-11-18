using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.Categories;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Categories screen's read model.
///
/// The tree is fetched FLAT and assembled in memory. A recursive CTE would also
/// work, but the flat query is one index scan and the assembly is O(n) over a
/// few hundred nodes; more importantly, the parent/child relationship then has
/// exactly one implementation instead of one in SQL and another in whatever
/// reads it. Every node comes back whether or not it is translated, because the
/// taxonomy is the same taxonomy in every language (see the handler).
///
/// R-17 applies: each SELECT sits directly above the positional record it
/// fills, and Dapper matches them BY POSITION.
/// </summary>
public sealed class CategoryStore(CatalogDbContext dbContext) : ICategoryStore
{
    private readonly CatalogDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <summary>
    /// Direct and subtree product counts for every node, in one pass.
    ///
    /// `d.path &lt;@ a.path` is "d is a descendant of a, or a itself", so the
    /// subtree figure includes the node's own products. Served by the GiST index
    /// on taxonomy_node.path added with this sprint — without it this is a
    /// sequential scan per node, which is invisible at 40 nodes and quadratic
    /// after that.
    /// </summary>
    private const string CountsCte = """
        WITH direct AS (
            SELECT p.primary_node_id AS node_id, count(*)::int AS n
            FROM catalog.product p
            GROUP BY p.primary_node_id
        ),
        subtree AS (
            SELECT a.id AS node_id, COALESCE(sum(d2.n), 0)::int AS n
            FROM catalog.taxonomy_node a
            LEFT JOIN catalog.taxonomy_node d ON d.path <@ a.path
            LEFT JOIN direct d2 ON d2.node_id = d.id
            GROUP BY a.id
        ),
        base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1)
        """;

    public async Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(
        DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        // Column order matches TreeRow exactly. R-17.
        const string sql = CountsCte + """

            SELECT n.id,
                   n.code,
                   COALESCE(tr.name, n.code)                      AS name,
                   tr.locale_code                                 AS name_locale,
                   n.node_type,
                   n.depth::int                                   AS depth,
                   n.status,
                   COALESCE(dr.n, 0)::int                         AS direct_product_count,
                   COALESCE(st.n, 0)::int                         AS subtree_product_count,
                   n.parent_id,
                   n.sort_order
            FROM catalog.taxonomy_node n
            LEFT JOIN LATERAL (
                SELECT tntr.name, tntr.locale_code
                FROM catalog.taxonomy_node_tr tntr
                WHERE tntr.node_id = n.id
                  AND tntr.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (tntr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            LEFT JOIN direct  dr ON dr.node_id = n.id
            LEFT JOIN subtree st ON st.node_id = n.id
            WHERE n.status = 'active'
            ORDER BY n.depth, n.sort_order, n.code
            """;

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var rows = await DapperGateway.QueryOnAsync<TreeRow>(
            connection, sql, new { displayLocale = locale.Code }, cancellationToken);

        return BuildTree(rows);
    }

    private sealed record TreeRow(
        long Id,
        string Code,
        string Name,
        string? NameLocale,
        string NodeType,
        int Depth,
        string Status,
        int DirectProductCount,
        int SubtreeProductCount,
        long? ParentId,
        int SortOrder);

    /// <summary>
    /// Flat rows to nested nodes, preserving the SQL's ordering at every level.
    ///
    /// A node whose parent is not in the result set (inactive, or filtered out)
    /// is promoted to the root rather than dropped. Dropping it would remove its
    /// entire subtree from the screen — products would exist in the catalogue
    /// with no path to reach them, and nothing would say so.
    ///
    /// Sibling order is the SQL's (sort_order, then code) and is NOT re-sorted
    /// here. LINQ's OrderByDescending is stable, so rows of equal depth keep the
    /// order the database gave them; a category tree's sequence is curated, and
    /// re-sorting it alphabetically in the client would quietly override that.
    /// </summary>
    private static IReadOnlyList<CategoryTreeNodeDto> BuildTree(IReadOnlyList<TreeRow> rows)
    {
        var childrenByParent = new Dictionary<long, List<CategoryTreeNodeDto>>();
        var present = rows.Select(r => r.Id).ToHashSet();

        // Deepest first, so a node's children are already built when it is.
        foreach (var row in rows.OrderByDescending(r => r.Depth))
        {
            childrenByParent.TryGetValue(row.Id, out var children);

            var node = new CategoryTreeNodeDto(
                row.Id,
                row.Code,
                row.Name,
                row.NameLocale,
                row.NodeType,
                row.Depth,
                row.Status,
                row.DirectProductCount,
                row.SubtreeProductCount,
                children is null ? [] : [.. children]);

            var parentKey = row.ParentId is long p && present.Contains(p) ? p : 0L;

            if (!childrenByParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                childrenByParent[parentKey] = siblings;
            }

            siblings.Add(node);
        }

        return childrenByParent.TryGetValue(0L, out var roots) ? [.. roots] : [];
    }

    public async Task<CategoryDetailDto?> GetDetailAsync(
        string code, DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var parameters = new { code, displayLocale = locale.Code };

        var head = await ReadHeadAsync(connection, parameters, cancellationToken);
        if (head is null)
        {
            return null;
        }

        var setsInUse = await ReadSetsInUseAsync(connection, parameters, cancellationToken);
        var labels = await ReadLabelsAsync(connection, parameters, cancellationToken);

        // Which template, and be explicit about where it came from. A declared
        // set wins; failing that, ONE set in use is a safe inference; several
        // is not, and is reported as such rather than merged.
        string source;
        string? templateSetCode;

        if (head.AttributeSetCode is not null)
        {
            source = TemplateSource.Declared;
            templateSetCode = head.AttributeSetCode;
        }
        else if (setsInUse.Count == 1)
        {
            source = TemplateSource.InUse;
            templateSetCode = setsInUse[0].Code;
        }
        else
        {
            source = setsInUse.Count == 0 ? TemplateSource.None : TemplateSource.Mixed;
            templateSetCode = null;
        }

        IReadOnlyList<AttributeTemplateRowDto> template = templateSetCode is null
            ? []
            : await ReadTemplateAsync(
                connection,
                new { code, displayLocale = locale.Code, setCode = templateSetCode },
                cancellationToken);

        return new CategoryDetailDto(
            head.Id,
            head.Code,
            head.Name,
            head.NameLocale,
            head.Description,
            head.NodeType,
            head.Depth,
            head.Status,
            head.ParentCode,
            head.ParentName,
            head.BrandName,
            head.DirectProductCount,
            head.SubtreeProductCount,
            head.AttributeSetCode ?? (source == TemplateSource.InUse ? setsInUse[0].Code : null),
            head.AttributeSetName ?? (source == TemplateSource.InUse ? setsInUse[0].Name : null),
            template.Count(t => t.IsRequired),
            source,
            setsInUse,
            template,
            labels);
    }

    // Column order matches HeadRow exactly. R-17.
    private static async Task<HeadRow?> ReadHeadAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = CountsCte + """

            SELECT n.id,
                   n.code,
                   COALESCE(tr.name, n.code)                       AS name,
                   tr.locale_code                                  AS name_locale,
                   tr.description,
                   n.node_type,
                   n.depth::int                                    AS depth,
                   n.status,
                   parent.code                                     AS parent_code,
                   COALESCE(ptr.name, parent.code)                 AS parent_name,
                   btr.name                                        AS brand_name,
                   COALESCE(dr.n, 0)::int                          AS direct_product_count,
                   COALESCE(st.n, 0)::int                          AS subtree_product_count,
                   aset.code                                       AS attribute_set_code,
                   aset.name                                       AS attribute_set_name
            FROM catalog.taxonomy_node n
            LEFT JOIN catalog.taxonomy_node parent ON parent.id = n.parent_id
            LEFT JOIN catalog.attribute_set aset   ON aset.id = n.attribute_set_id
            LEFT JOIN LATERAL (
                SELECT tntr.name, tntr.description, tntr.locale_code
                FROM catalog.taxonomy_node_tr tntr
                WHERE tntr.node_id = n.id
                  AND tntr.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (tntr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            LEFT JOIN LATERAL (
                SELECT tntr.name
                FROM catalog.taxonomy_node_tr tntr
                WHERE tntr.node_id = parent.id
                  AND tntr.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (tntr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) ptr ON true
            LEFT JOIN LATERAL (
                SELECT btr2.name
                FROM catalog.brand_tr btr2
                WHERE btr2.brand_id = n.brand_id
                  AND btr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (btr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) btr ON true
            LEFT JOIN direct  dr ON dr.node_id = n.id
            LEFT JOIN subtree st ON st.node_id = n.id
            WHERE n.code = @code AND n.status = 'active'
            """;

        return await DapperGateway.QuerySingleOrDefaultOnAsync<HeadRow>(
            connection, sql, parameters, cancellationToken);
    }

    private sealed record HeadRow(
        long Id,
        string Code,
        string Name,
        string? NameLocale,
        string? Description,
        string NodeType,
        int Depth,
        string Status,
        string? ParentCode,
        string? ParentName,
        string? BrandName,
        int DirectProductCount,
        int SubtreeProductCount,
        string? AttributeSetCode,
        string? AttributeSetName);

    /// <summary>
    /// The node's attribute template, with real coverage.
    ///
    /// Coverage counts products in the node's SUBTREE that hold an approved
    /// value for the attribute, over the products in that subtree — the same
    /// rule the grid's completeness bar and the cockpit's average use (approved
    /// values, base locale or language-neutral). Three screens, one definition;
    /// if they ever disagree the number is worthless on all three.
    ///
    /// Returns the template even when no product sits underneath the node, with
    /// coverage 0 and productsInScope 0. That pair is readable as "nothing to
    /// measure"; a 0% bar with no denominator would read as "nobody has filled
    /// this in", which is a different and wrong statement.
    ///
    /// Column order matches TemplateRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeTemplateRowDto>> ReadTemplateAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1),
            node AS (
                SELECT id, path
                FROM catalog.taxonomy_node
                WHERE code = @code AND status = 'active'
            ),
            template_set AS (
                SELECT id FROM catalog.attribute_set WHERE code = @setCode
            ),
            scope AS (
                SELECT p.id, p.base_locale
                FROM catalog.product p
                JOIN catalog.taxonomy_node d ON d.id = p.primary_node_id
                JOIN node ON d.path <@ node.path
            ),
            scope_size AS (SELECT count(*)::int AS n FROM scope)
            SELECT a.code,
                   COALESCE(atr.label, a.code)                       AS label,
                   atr.locale_code                                  AS label_locale,
                   COALESCE(g.code, '')                              AS group_code,
                   COALESCE(gtr.name, g.code, '')                    AS group_label,
                   a.data_type,
                   sa.is_required,
                   CASE WHEN (SELECT n FROM scope_size) = 0 THEN 0
                        ELSE round(100.0 * (
                            SELECT count(DISTINCT av.product_id)
                            FROM catalog.attribute_value av
                            JOIN scope s ON s.id = av.product_id
                            WHERE av.attribute_id = a.id
                              AND av.data_state = 'approved'
                              AND (av.locale_code IS NULL OR av.locale_code = s.base_locale)
                        ) / (SELECT n FROM scope_size))
                   END::int                                          AS coverage_pct,
                   (SELECT n FROM scope_size)                        AS products_in_scope
            FROM node
            JOIN template_set ts ON true
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = ts.id
            JOIN catalog.attribute a                ON a.id = sa.attribute_id
            LEFT JOIN catalog.attribute_group g     ON g.id = a.group_id
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
            ORDER BY sa.is_required DESC, sa.sort_order, a.code
            """;

        var rows = await DapperGateway.QueryOnAsync<TemplateRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeTemplateRowDto(
            r.Code, r.Label, r.LabelLocale, r.GroupCode, r.GroupLabel,
            r.DataType, r.IsRequired, r.CoveragePct, r.ProductsInScope))];
    }

    private sealed record TemplateRow(
        string Code,
        string Label,
        string? LabelLocale,
        string GroupCode,
        string GroupLabel,
        string DataType,
        bool IsRequired,
        int CoveragePct,
        int ProductsInScope);

    /// <summary>
    /// The attribute sets the products beneath this node were actually created
    /// with, busiest first.
    ///
    /// A fact with no interpretation attached: the caller decides whether one
    /// set is enough to call it this branch's template. Column order matches
    /// SetUsageRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<AttributeSetUsageDto>> ReadSetsInUseAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH node AS (
                SELECT path FROM catalog.taxonomy_node
                WHERE code = @code AND status = 'active'
            )
            SELECT s.code, s.name, count(*)::int AS product_count
            FROM catalog.product p
            JOIN catalog.taxonomy_node d ON d.id = p.primary_node_id
            JOIN node ON d.path <@ node.path
            JOIN catalog.attribute_set s ON s.id = p.attribute_set_id
            GROUP BY s.code, s.name
            ORDER BY count(*) DESC, s.code
            """;

        var rows = await DapperGateway.QueryOnAsync<SetUsageRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new AttributeSetUsageDto(r.Code, r.Name, r.ProductCount))];
    }

    private sealed record SetUsageRow(string Code, string Name, int ProductCount);

    /// <summary>
    /// One row per CONFIGURED locale, including the ones with no translation.
    ///
    /// Left join from locale, not inner join from the translation table: a
    /// language with nothing in it has to appear as an empty row, because that
    /// is precisely the fact somebody opens this panel to find out. Column
    /// order matches LabelRow exactly. R-17.
    /// </summary>
    private static async Task<IReadOnlyList<CategoryLabelDto>> ReadLabelsAsync(
        NpgsqlConnection connection, object parameters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.code                                   AS locale_code,
                   l.name                                   AS locale_name,
                   l.is_base,
                   tr.name,
                   COALESCE(tr.translation_status, 'missing') AS translation_status
            FROM catalog.locale l
            LEFT JOIN catalog.taxonomy_node_tr tr
                   ON tr.locale_code = l.code
                  AND tr.node_id = (SELECT id FROM catalog.taxonomy_node
                                     WHERE code = @code AND status = 'active')
            WHERE l.is_active
            ORDER BY l.is_base DESC, l.code
            """;

        var rows = await DapperGateway.QueryOnAsync<LabelRow>(
            connection, sql, parameters, cancellationToken);

        return [.. rows.Select(r => new CategoryLabelDto(
            r.LocaleCode, r.LocaleName, r.IsBase, r.Name, r.TranslationStatus))];
    }

    private sealed record LabelRow(
        string LocaleCode,
        string LocaleName,
        bool IsBase,
        string? Name,
        string TranslationStatus);
}
