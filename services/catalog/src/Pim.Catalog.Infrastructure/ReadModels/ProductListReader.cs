using System.Collections.Frozen;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Set-based read model for the products grid.
///
/// Deliberately raw ADO.NET rather than EF LINQ or SqlQueryRaw: the query uses
/// lateral joins and a CTE that LINQ cannot express, and EF's raw-SQL
/// materialization does not support unmapped result types. The DbContext
/// supplies the connection so the read joins any ambient transaction.
///
/// Shape matters for performance: the page's ids are selected FIRST using
/// indexable columns, and the derived columns (completeness, languages,
/// variant count) are computed only for that page.
///
/// Completeness follows the DB design's computed rule — approved values for
/// required attributes ÷ required attributes in the product's set — always
/// from the EAV, never from attributes_cache.
/// </summary>
public sealed class ProductListReader(CatalogDbContext dbContext) : IProductListReader
{
    /// <summary>
    /// Sort expressions inside the paging CTE. These, and the outer set below,
    /// are the ONLY strings ever interpolated into SQL; every user value
    /// travels as an NpgsqlParameter.
    /// </summary>
    private static readonly FrozenDictionary<string, string> PageSortExpressions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "p.product_code",
            ["name"] = "name",
            ["category"] = "category",
            ["status"] = "p.workflow_state",
            ["quality"] = "COALESCE(comp.pct, 0)",
            ["owner"] = "p.owner_display_name",
            ["updated"] = "p.updated_at",
            ["relevance"] = SearchRankExpression,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The same order re-applied in the outer projection.</summary>
    private static readonly FrozenDictionary<string, string> OuterSortExpressions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "p.product_code",
            ["name"] = "pg.name",
            ["category"] = "pg.category",
            ["status"] = "p.workflow_state",
            ["quality"] = "COALESCE(comp.pct, 0)",
            ["owner"] = "p.owner_display_name",
            ["updated"] = "p.updated_at",
            ["relevance"] = SearchRankExpression,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Weighted rank of the row against the search query.
    ///
    /// ts_rank_cd rather than ts_rank: cover density rewards matches that sit
    /// close together, which is what makes "craft abdeckplatte" beat a product
    /// that merely mentions both words in unrelated fields. Evaluates to 0 when
    /// there is no search term, which is why sortBy=relevance without a search
    /// is rejected by the validator rather than silently returning arbitrary
    /// order.
    /// </summary>
    private const string SearchRankExpression =
        "ts_rank_cd(p.search_vector, catalog.product_search_query(@searchRaw))";

    /// <summary>
    /// The product's name in the requested locale, falling back to its base
    /// locale, and reporting which one it actually got.
    ///
    /// A lateral rather than the join this replaced. The join could only ever
    /// match one locale, which is why the grid showed German for fifteen
    /// sprints: every seeded product's base locale is de-DE, and the English
    /// text sitting in product_tr was reachable only from the Localization tab.
    ///
    /// `ORDER BY (locale_code = @displayLocale) DESC LIMIT 1` is the fallback:
    /// true sorts before false, so the requested locale wins when it exists and
    /// the base locale is taken when it does not. Restricting the IN list to
    /// those two locales keeps the scan on the (product_id, locale_code) key
    /// instead of reading every translation of the row.
    ///
    /// `locale_code` is projected because the caller cannot otherwise tell a
    /// translation from a substitute, and the UI marks the difference
    /// (decision 2026-08-17).
    /// </summary>
    private const string ProductNameLateral = """
            LEFT JOIN LATERAL (
                SELECT ptr.name, ptr.locale_code
                FROM catalog.product_tr ptr
                WHERE ptr.product_id = p.id
                  AND ptr.locale_code IN (@displayLocale, p.base_locale)
                ORDER BY (ptr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            """;

    /// <summary>Same fallback rule for the category label.</summary>
    private const string CategoryNameLateral = """
            LEFT JOIN LATERAL (
                SELECT tntr.name, tntr.locale_code
                FROM catalog.taxonomy_node_tr tntr
                WHERE tntr.node_id = tn.id
                  AND tntr.locale_code IN (@displayLocale, p.base_locale)
                ORDER BY (tntr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) ntr ON true
            """;

    /// <summary>
    /// Completeness per the DB design: approved values for the product's
    /// required attributes / required attributes in its set. Served by
    /// ix_av_product_attr.
    /// </summary>
    private const string CompletenessLateral = """
            LEFT JOIN LATERAL (
                SELECT round(100.0 * count(av.id) / nullif(count(sa.attribute_id), 0)) AS pct
                FROM catalog.attribute_set_attribute sa
                LEFT JOIN catalog.attribute_value av
                       ON av.product_id = p.id
                      AND av.attribute_id = sa.attribute_id
                      AND av.data_state = 'approved'
                      -- Base locale or language-neutral only. A localizable
                      -- required attribute translated into three languages
                      -- would otherwise count three times and push
                      -- completeness above 100%.
                      --
                      -- Deliberately NOT @displayLocale, and this is the one
                      -- place in this file where the two differ. Completeness
                      -- answers "is this product finished?", which is a
                      -- property of the product, not of who is looking at it —
                      -- so the score must not move when the reader changes
                      -- language. Making it per-locale is ADR-008, decided and
                      -- scheduled for a later sprint precisely so that it does
                      -- not land in the same gate as this change.
                      AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
                WHERE sa.set_id = p.attribute_set_id AND sa.is_required
            ) comp ON true
            """;

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "No user input reaches the command text: ORDER BY comes from a " +
                        "fixed whitelist dictionary and every filter value is an NpgsqlParameter.")]
    public async Task<PagedResult<ProductListItemDto>> ListAsync(
        ProductListFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!PageSortExpressions.TryGetValue(filter.SortBy, out var pageSort) ||
            !OuterSortExpressions.TryGetValue(filter.SortBy, out var outerSort))
        {
            throw new ArgumentException($"Unwhitelisted sort key '{filter.SortBy}'.", nameof(filter));
        }

        var direction = filter.SortDescending ? "DESC" : "ASC";
        var (whereClause, filterParameters) = BuildWhere(filter);

        // Completeness is only needed inside the paging CTE when it drives the
        // ORDER BY. Computing it for every matching row otherwise made the grid
        // O(rows) instead of O(page): 12s at ~2,900 products before this change.
        var sortsByCompleteness = filter.SortBy == "quality";
        var pageCompletenessJoin = sortsByCompleteness ? CompletenessLateral : string.Empty;

        // Two-phase: pick the page's ids using only indexable columns, then
        // compute the derived columns for those (<= 100) rows.
        var sql = $"""
            WITH page AS (
                SELECT p.id,
                       COALESCE(tr.name, p.product_code) AS name,
                       COALESCE(ntr.name, tn.code)       AS category,
                       tr.locale_code                    AS name_locale,
                       ntr.locale_code                   AS category_locale
                FROM catalog.product p
                JOIN catalog.taxonomy_node tn   ON tn.id = p.primary_node_id
                {ProductNameLateral}
                {CategoryNameLateral}
                {pageCompletenessJoin}
                {whereClause}
                ORDER BY {pageSort} {direction}, p.id ASC
                LIMIT @pageSize OFFSET @offset
            )
            SELECT
                p.public_id,
                p.product_code,
                pg.name,
                pg.category,
                p.lifecycle_state,
                p.workflow_state,
                COALESCE(comp.pct, 0)::int         AS completeness_pct,
                COALESCE(langs.codes, '')          AS languages_csv,
                COALESCE(sk.cnt, 0)::int           AS variant_count,
                p.owner_display_name,
                p.updated_at,
                -- LAST, and deliberately so. ProductRow is a positional record,
                -- and Dapper materialises those by matching constructor
                -- parameters to columns BY POSITION, not by name. This column
                -- list and ProductRow's parameter order are therefore one
                -- coupled unit: inserting a column in the middle of either
                -- without the other fails EVERY row, not some of them, with a
                -- "no matching constructor" that names the SQL's order rather
                -- than the record's. Sprint 16 added these two in the middle
                -- here and at the end there, and took out all 20 reader tests.
                pg.name_locale,
                pg.category_locale
            FROM page pg
            JOIN catalog.product p ON p.id = pg.id
            LEFT JOIN LATERAL (
                SELECT count(*) AS cnt FROM catalog.sku s WHERE s.product_id = p.id
            ) sk ON true
            LEFT JOIN LATERAL (
                SELECT string_agg(ptr.locale_code, ',' ORDER BY ptr.locale_code) AS codes
                FROM catalog.product_tr ptr WHERE ptr.product_id = p.id
            ) langs ON true
            {CompletenessLateral}
            ORDER BY {outerSort} {direction}, p.id ASC
            """;

        var pageArgs = ToDynamicParameters(filterParameters);
        pageArgs.Add("pageSize", filter.PageSize);
        pageArgs.Add("offset", (long)(filter.Page - 1) * filter.PageSize);

        // Already resolved against catalog.locale by the handler, and travelling
        // as a parameter regardless — it is never interpolated into the command
        // text, so an unexpected value can only ever match no rows.
        pageArgs.Add("displayLocale", filter.DisplayLocale.Code);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var rows = await DapperGateway.QueryOnAsync<ProductRow>(
                connection, sql, pageArgs, cancellationToken);

            var items = rows.Select(row =>
            {
                var workflow = NormalizeWorkflow(row.WorkflowState);

                return new ProductListItemDto(
                    row.PublicId,
                    row.ProductCode,
                    row.Name,
                    row.Category,
                    NormalizeLifecycle(row.LifecycleState),
                    workflow,
                    row.CompletenessPct,
                    row.LanguagesCsv.Length == 0 ? [] : row.LanguagesCsv.Split(','),
                    row.VariantCount,
                    row.OwnerDisplayName,
                    row.UpdatedAt,
                    ChannelReadiness.DeriveAll(row.CompletenessPct, workflow),
                    row.NameLocale,
                    row.CategoryLocale);
            }).ToList();

            // Counted separately rather than with COUNT(*) OVER (): the window
            // function forced the derived-column laterals to run for every
            // matching row, not just the page.
            var totalCount = await CountAsync(connection, whereClause, filter, cancellationToken);

            return new PagedResult<ProductListItemDto>(items, filter.Page, filter.PageSize, totalCount);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// One grid row as projected. Lifecycle and workflow arrive in the
    /// database's casing and are normalised on the way out; completeness feeds
    /// both the column and the derived readiness, so it is read once.
    /// </summary>
    private sealed record ProductRow(
        Guid PublicId,
        string ProductCode,
        string Name,
        string Category,
        string LifecycleState,
        string WorkflowState,
        int CompletenessPct,
        string LanguagesCsv,
        int VariantCount,
        string? OwnerDisplayName,
        DateTimeOffset UpdatedAt,
        /// <summary>Null when neither the requested nor the base locale had a
        /// translation and the product code is standing in for a name.</summary>
        string? NameLocale,
        string? CategoryLocale);

    /// <summary>
    /// The WHERE clause is assembled conditionally, so its values arrive as
    /// NpgsqlParameters rather than an anonymous object. Handing Dapper the
    /// same instances keeps one source of truth for the filter.
    /// </summary>
    private static DynamicParameters ToDynamicParameters(IEnumerable<NpgsqlParameter> parameters)
    {
        var bag = new DynamicParameters();
        foreach (var parameter in parameters)
        {
            bag.Add(parameter.ParameterName, parameter.Value);
        }

        return bag;
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Where clause is composed of fixed fragments; all values are parameters.")]
    private static async Task<int> CountAsync(
        NpgsqlConnection connection,
        string whereClause,
        ProductListFilter filter,
        CancellationToken cancellationToken)
    {
        // The same fallback lateral as the page query, not the simpler join it
        // replaced: a WHERE clause that filters on tr.name must see exactly the
        // rows the page sees, or the total would count a different set from the
        // one it claims to describe.
        var sql = $"""
            SELECT COUNT(*)::int
            FROM catalog.product p
            JOIN catalog.taxonomy_node tn   ON tn.id = p.primary_node_id
            {ProductNameLateral}
            {whereClause}
            """;

        // Rebuilt from the same filter, so the count can never diverge from the
        // page it counts.
        var (_, parameters) = BuildWhere(filter);

        var args = ToDynamicParameters(parameters);
        args.Add("displayLocale", filter.DisplayLocale.Code);

        return await DapperGateway.QuerySingleOrDefaultOnAsync<int>(
            connection, sql, args, cancellationToken);
    }

    /// <summary>Builds the WHERE clause and its parameters (fresh instances per command).</summary>
    private static (string WhereClause, List<NpgsqlParameter> Parameters) BuildWhere(ProductListFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (filter.WorkflowState is not null)
        {
            conditions.Add("p.workflow_state = @workflowState");
            parameters.Add(new NpgsqlParameter("workflowState",
                filter.WorkflowState.Value.ToString().ToLowerInvariant()));
        }

        if (filter.CategoryId is not null)
        {
            // Subtree semantics: filtering by a category includes its descendants.
            conditions.Add("tn.path <@ (SELECT path FROM catalog.taxonomy_node WHERE id = @categoryId)");
            parameters.Add(new NpgsqlParameter("categoryId", filter.CategoryId.Value));
        }

        if (filter.Codes is { Count: > 0 })
        {
            conditions.Add("p.product_code = ANY(@codes)");
            parameters.Add(new NpgsqlParameter("codes", filter.Codes.ToArray()));
        }

        // Always bound, even when there is no search: the relevance sort
        // expression names it, and Npgsql throws on a parameter that appears in
        // the command text but was never added.
        parameters.Add(new NpgsqlParameter("searchRaw", filter.Search ?? string.Empty));

        if (filter.Search is not null)
        {
            // HYBRID, and deliberately so. The two mechanisms cover different
            // things a steward means by "search", and neither covers both:
            //
            //   FTS    — whole words, stemmed, in any order. Finds "Fliese"
            //            when the product says "Fliesen", and matches
            //            "craft abdeckplatte" across two separate fields.
            //   ILIKE  — substrings at any position. Finds "Abdeck" inside
            //            "Abdeckplatte", and "-31-X-" inside a product code.
            //            FTS cannot: it only ever matches whole lexemes.
            //
            // Dropping the ILIKE branch would have been a regression for
            // partial-code lookups, which is how this catalogue is actually
            // searched. The GIN index serves the FTS branch; the ILIKE branch
            // still scans, so this is a matching-quality change, not a
            // performance one.
            //
            // MATCHING IS LOCALE-AGNOSTIC; RENDERING IS NOT. The ILIKE branch
            // searches every product_tr row rather than the one the display
            // lateral picked. Scoping it to the displayed name — which is what
            // Sprint 16 did, by reaching for `tr.name` because it was in scope
            // — produced a split nobody decided: a whole German word still
            // matched, because search_vector aggregates across locales, while
            // a German *fragment* silently stopped matching as soon as the
            // grid defaulted to English. Same query, same catalogue, different
            // answer depending on a view setting. EXISTS restores the Sprint 14
            // behaviour and makes the two branches agree with each other
            // (Sprint 17; caught by the smoke suite, not by review).
            conditions.Add("""
                (p.search_vector @@ catalog.product_search_query(@searchRaw)
                 OR p.product_code ILIKE @search
                 OR EXISTS (
                        SELECT 1
                        FROM catalog.product_tr ptr_search
                        WHERE ptr_search.product_id = p.id
                          AND ptr_search.name ILIKE @search))
                """);
            parameters.Add(new NpgsqlParameter("search", $"%{EscapeLike(filter.Search)}%"));
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : string.Empty;
        return (whereClause, parameters);
    }

    /// <summary>DB stores 'ACTIVE'/'PHASE_OUT'; the API exposes enum names.</summary>
    private static string NormalizeLifecycle(string value) => value switch
    {
        "PLANNED" => "Planned",
        "ACTIVE" => "Active",
        "PHASE_OUT" => "PhaseOut",
        "DISCONTINUED" => "Discontinued",
        _ => value,
    };

    /// <summary>DB stores lowercase workflow states; the API exposes enum names.</summary>
    private static string NormalizeWorkflow(string value) =>
        ProductWriteScope.NormalizeWorkflow(value);

    private static string EscapeLike(string input) =>
        input.Replace(@"\", @"\\", StringComparison.Ordinal)
             .Replace("%", @"\%", StringComparison.Ordinal)
             .Replace("_", @"\_", StringComparison.Ordinal);
}
