using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Translations;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Translation Workbench's read model.
///
/// "Outdated" is the only figure here that is not simply counted, and it is the
/// one worth understanding. A translation is outdated when the BASE-locale text
/// has been edited more recently than the translation of it — literally
/// `target.updated_at &lt; base.updated_at` on `product_tr`. That column is
/// maintained by the domain: <c>ProductTranslation.Rename</c> stamps
/// <c>UpdatedAtUtc</c> on every write, so editing a German master immediately
/// marks its English and French copies as stale, with no extra bookkeeping and
/// nothing to keep in sync.
///
/// It is therefore also provable: the smoke suite edits a product's base name
/// and asserts the same product appears as outdated afterwards. A figure that
/// only ever reads zero is indistinguishable from a broken query (R-19).
///
/// R-17 applies: every SELECT sits directly above the positional record it
/// fills, and Dapper matches them BY POSITION.
/// </summary>
public sealed class TranslationWorkbenchStore(CatalogDbContext dbContext) : ITranslationWorkbenchStore
{
    private readonly CatalogDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <summary>
    /// What this build does not draw, and why. Carried in the payload rather
    /// than hard-coded in the component, so the panel appears when the reason
    /// disappears — same rule as the cockpit's omissions.
    /// </summary>
    private static readonly IReadOnlyList<TranslationOmittedPanelDto> OmittedPanels =
    [
        new("Run AI batch",
            "There is no machine-translation service wired to this build. A button that "
            + "did nothing, or that queued work nothing would pick up, is worse than its absence."),
        new("AI confidence",
            "v4.2 shows an average confidence per suggestion. Nothing here produces "
            + "suggestions, so every number in that card would be invented."),
        new("Approved in the last 30 days",
            "product_tr records when a row was last written, not when it was approved. "
            + "Counting writes and calling them approvals would overstate review activity "
            + "by every save that was never reviewed."),
    ];

    public async Task<TranslationWorkbenchDto> GetWorkbenchAsync(
        DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        // One pass over the catalogue per locale. Every figure the screen shows —
        // the KPI row included — is summed from THESE rows in the client, so the
        // headline and the breakdown cannot disagree.
        //
        // Column order matches ProgressRow exactly. R-17.
        const string sql = """
            WITH totals AS (SELECT count(*)::int AS n FROM catalog.product)
            SELECT l.code,
                   l.name,
                   l.is_base,
                   (SELECT n FROM totals)                                  AS total_products,
                   count(t.product_id)::int                                AS with_text,
                   ((SELECT n FROM totals) - count(t.product_id))::int     AS missing,
                   count(*) FILTER (
                       WHERE t.product_id IS NOT NULL
                         AND b.updated_at IS NOT NULL
                         AND t.updated_at < b.updated_at)::int             AS outdated,
                   count(*) FILTER (
                       WHERE t.product_id IS NOT NULL
                         AND t.translation_status IS DISTINCT FROM 'approved')::int AS awaiting_review,
                   count(*) FILTER (
                       WHERE t.product_id IS NOT NULL
                         AND t.translation_status = 'approved')::int       AS approved
            FROM catalog.locale l
            CROSS JOIN catalog.product p
            LEFT JOIN catalog.product_tr t ON t.product_id = p.id AND t.locale_code = l.code
            LEFT JOIN catalog.product_tr b ON b.product_id = p.id AND b.locale_code = p.base_locale
            WHERE l.is_active
            GROUP BY l.code, l.name, l.is_base
            ORDER BY l.is_base DESC, l.code
            """;

        var rows = await DapperGateway.QueryOnAsync<ProgressRow>(
            connection, sql, parameters: null, cancellationToken);

        var baseRow = rows.FirstOrDefault(r => r.IsBase);

        var locales = rows
            .Select(r => new LocaleProgressDto(
                r.Code, r.Name, r.IsBase, r.TotalProducts, r.WithText, r.Missing,
                r.Outdated, r.AwaitingReview, r.Approved,
                r.TotalProducts == 0 ? 0 : (int)Math.Round(100.0 * r.WithText / r.TotalProducts)))
            .ToList();

        return new TranslationWorkbenchDto(
            baseRow?.Code ?? string.Empty,
            baseRow?.Name ?? string.Empty,
            rows.Count > 0 ? rows[0].TotalProducts : 0,
            locales,
            OmittedPanels);
    }

    private sealed record ProgressRow(
        string Code,
        string Name,
        bool IsBase,
        int TotalProducts,
        int WithText,
        int Missing,
        int Outdated,
        int AwaitingReview,
        int Approved);

    /// <summary>
    /// The per-product state of one target locale, filtered, searched and paged.
    ///
    /// The FROM clause is shared between the count and the page so the two can
    /// never describe different sets — a total that disagrees with the rows
    /// under it is how paging quietly breaks.
    /// </summary>
    public async Task<TranslationQueueDto> GetQueueAsync(
        string target,
        string state,
        string? search,
        int page,
        int pageSize,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var stateFilter = state switch
        {
            "missing" => "t.product_id IS NULL",
            "outdated" => "t.product_id IS NOT NULL AND b.updated_at IS NOT NULL AND t.updated_at < b.updated_at",
            "review" => "t.product_id IS NOT NULL AND t.translation_status IS DISTINCT FROM 'approved'",
            "all" => "true",
            // "attention" is the default and the reason the screen exists: every
            // product whose target text is absent, stale, or not yet approved.
            _ => """
                 (t.product_id IS NULL
                  OR t.translation_status IS DISTINCT FROM 'approved'
                  OR (b.updated_at IS NOT NULL AND t.updated_at < b.updated_at))
                 """,
        };

        var searchFilter = search is null
            ? string.Empty
            : """
              AND (p.product_code ILIKE @search
                   OR EXISTS (SELECT 1 FROM catalog.product_tr s
                               WHERE s.product_id = p.id AND s.name ILIKE @search))
              """;

        // Matching is locale-agnostic, rendering is not — the rule Sprint 17
        // wrote down after substring search silently stopped working across
        // locales. Searching only the displayed name would make a product
        // findable or not depending on a view setting.
        //
        // Two FROM clauses, sharing one WHERE. The count does not need the
        // display-name laterals, and computing a fallback name for every
        // matching row only to discard it is the kind of waste that shows up
        // once the catalogue is large. The predicate is built once and
        // interpolated into both, so the count and the page cannot describe
        // different sets.
        var joins = """
            FROM catalog.product p
            JOIN catalog.taxonomy_node tn ON tn.id = p.primary_node_id
            LEFT JOIN catalog.product_tr t ON t.product_id = p.id AND t.locale_code = @target
            LEFT JOIN catalog.product_tr b ON b.product_id = p.id AND b.locale_code = p.base_locale
            """;

        var displayJoins = """
            LEFT JOIN LATERAL (
                SELECT ptr.name, ptr.locale_code
                FROM catalog.product_tr ptr
                WHERE ptr.product_id = p.id
                  AND ptr.locale_code IN (@displayLocale, p.base_locale)
                ORDER BY (ptr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            LEFT JOIN LATERAL (
                SELECT tntr.name
                FROM catalog.taxonomy_node_tr tntr
                WHERE tntr.node_id = tn.id
                  AND tntr.locale_code IN (@displayLocale, p.base_locale)
                ORDER BY (tntr.locale_code = @displayLocale) DESC
                LIMIT 1
            ) ntr ON true
            """;

        var where = $"""
            WHERE {stateFilter}
            {searchFilter}
            """;

        var parameters = new
        {
            target,
            displayLocale = locale.Code,
            search = search is null ? null : $"%{search}%",
            limit = pageSize,
            offset = (page - 1) * pageSize,
        };

        // int?, not int: with T = int the gateway's Task<T?> is still int, and
        // the null-coalesce below would be flagged as unreachable by the
        // warnings-as-errors build.
        var totalCount = await DapperGateway.QuerySingleOrDefaultOnAsync<int?>(
            connection, $"SELECT count(*)::int {joins} {where}", parameters, cancellationToken) ?? 0;

        // Column order matches QueueRow exactly. R-17.
        var sql = $"""
            SELECT p.product_code,
                   COALESCE(tr.name, p.product_code)  AS name,
                   tr.locale_code                     AS name_locale,
                   COALESCE(ntr.name, tn.code)        AS category,
                   CASE WHEN t.product_id IS NULL THEN 'missing'
                        WHEN b.updated_at IS NOT NULL AND t.updated_at < b.updated_at THEN 'outdated'
                        ELSE t.translation_status
                   END                                AS state,
                   t.translation_status,
                   t.source,
                   t.updated_at                       AS target_updated_at_utc,
                   b.updated_at                       AS base_updated_at_utc
            {joins}
            {displayJoins}
            {where}
            ORDER BY CASE WHEN t.product_id IS NULL THEN 0
                          WHEN b.updated_at IS NOT NULL AND t.updated_at < b.updated_at THEN 1
                          WHEN t.translation_status IS DISTINCT FROM 'approved' THEN 2
                          ELSE 3 END,
                     p.product_code
            LIMIT @limit OFFSET @offset
            """;

        var rows = await DapperGateway.QueryOnAsync<QueueRow>(
            connection, sql, parameters, cancellationToken);

        var items = rows
            .Select(r => new TranslationQueueItemDto(
                r.ProductCode, r.Name, r.NameLocale, r.Category, r.State,
                r.TranslationStatus, r.Source, r.TargetUpdatedAtUtc, r.BaseUpdatedAtUtc))
            .ToList();

        return new TranslationQueueDto(target, state, page, pageSize, totalCount, items);
    }

    private sealed record QueueRow(
        string ProductCode,
        string Name,
        string? NameLocale,
        string Category,
        string State,
        string? TranslationStatus,
        string? Source,
        DateTimeOffset? TargetUpdatedAtUtc,
        DateTimeOffset? BaseUpdatedAtUtc);
}
