using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Dashboard;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Product Cockpit's figures.
///
/// Every number here is computed from the catalogue at request time. Nothing is
/// cached and nothing is stored: a dashboard that reports yesterday's figure
/// without saying so is worse than one that takes an extra 80ms, because the
/// staleness is invisible and the reader has no way to ask.
///
/// Completeness is computed with the SAME rule as the grid — approved values
/// for required attributes, base locale or language-neutral. That is not a
/// coincidence to be maintained by hand: if the cockpit's average and the
/// grid's per-row percentages ever disagree, both become untrustworthy, and the
/// smoke suite asserts they agree.
///
/// R-17 applies throughout. Dapper materialises positional records by matching
/// constructor parameters to columns BY POSITION, so each SELECT below sits
/// directly above the record it fills and the two must be edited together.
/// </summary>
public sealed class DashboardStore(CatalogDbContext dbContext) : IDashboardStore
{
    /// <summary>
    /// Number of activity lines. Small on purpose: this is a glance, and the
    /// Activity tab already exists for anyone who wants the history.
    /// </summary>
    private const int ActivityLimit = 12;

    public async Task<ProductCockpitDto> GetAsync(
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var totals = await ReadTotalsAsync(connection, cancellationToken);
            var bands = await ReadQualityBandsAsync(connection, totals.TotalProducts, cancellationToken);
            var areas = await ReadCompletenessByAreaAsync(connection, locale, cancellationToken);
            var pipeline = await ReadWorkflowPipelineAsync(connection, cancellationToken);
            var locales = await ReadLocaleCoverageAsync(connection, totals.TotalProducts, cancellationToken);
            var activity = await ReadRecentActivityAsync(connection, cancellationToken);

            return new ProductCockpitDto(
                BuildKpis(totals),
                bands,
                areas,
                pipeline,
                locales,
                activity,
                OmittedPanels);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    // ---------------------------------------------------------------- KPIs --

    /// <summary>
    /// v4.2's four KPI cards, three of them unchanged.
    ///
    /// The fourth in the prototype is "Channel Errors", which has no service
    /// behind it. Rather than leave a hole in a four-card row, it is replaced by
    /// "Ready to publish" — which is not an invention either: it is one of
    /// v4.2's own saved product views (<c>Approved</c> and completeness ≥ 90),
    /// so it is the prototype's own vocabulary and its data exists today.
    /// </summary>
    private static IReadOnlyList<DashboardKpiDto> BuildKpis(TotalsRow t) =>
    [
        new("total_products", "Total products",
            t.TotalProducts.ToString("N0", CultureInfo.InvariantCulture),
            $"{t.TotalSkus:N0} SKUs"),

        new("avg_completeness", "Avg completeness",
            $"{t.AvgCompleteness}%",
            "approved values for required attributes"),

        new("pending_translation", "Pending translation",
            t.PendingTranslation.ToString("N0", CultureInfo.InvariantCulture),
            "products missing at least one configured locale"),

        new("ready_to_publish", "Ready to publish",
            t.ReadyToPublish.ToString("N0", CultureInfo.InvariantCulture),
            "approved and at least 90% complete"),
    ];

    private static readonly IReadOnlyList<OmittedPanelDto> OmittedPanels =
    [
        new("Channel errors",
            "No channel service exists. A count here would be invented, and an invented "
          + "number on a dashboard is believed — it is read at a glance by people who did "
          + "not build it."),
        new("Channel sync status",
            "Same reason. Per-channel progress bars need a channel service to report progress."),
        new("Catalogue completeness — last 12 weeks",
            "Nothing snapshots completeness over time. The audit trail records what changed, "
          + "not what the catalogue scored on a given day, so this line could only be drawn "
          + "by back-filling a plausible shape."),
        new("My work queue",
            "Two things missing rather than one: no workflow engine to produce tasks, and no "
          + "authentication, so there is no 'my'."),
    ];

    // -------------------------------------------------------------- totals --

    /// <summary>
    /// Column order below matches <see cref="TotalsRow"/> exactly. R-17.
    ///
    /// One pass over the catalogue rather than four. Completeness is the
    /// expensive part — a per-product aggregate over the required attributes of
    /// its set — so it is computed once in a CTE and then read four ways.
    /// </summary>
    private static async Task<TotalsRow> ReadTotalsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH product_completeness AS (
                SELECT p.id,
                       p.workflow_state,
                       COALESCE(comp.pct, 0)::int AS pct
                FROM catalog.product p
                LEFT JOIN LATERAL (
                    SELECT round(100.0 * count(av.id) / nullif(count(sa.attribute_id), 0)) AS pct
                    FROM catalog.attribute_set_attribute sa
                    LEFT JOIN catalog.attribute_value av
                           ON av.product_id = p.id
                          AND av.attribute_id = sa.attribute_id
                          AND av.data_state = 'approved'
                          AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
                    WHERE sa.set_id = p.attribute_set_id AND sa.is_required
                ) comp ON true
            ),
            locale_count AS (SELECT count(*) AS n FROM catalog.locale)
            SELECT
                (SELECT count(*) FROM catalog.product)::int                        AS total_products,
                (SELECT count(*) FROM catalog.sku)::int                            AS total_skus,
                COALESCE((SELECT round(avg(pct)) FROM product_completeness), 0)::int AS avg_completeness,
                -- A product is "pending translation" when it does not have a
                -- product_tr row in every configured locale. Counted against the
                -- locales the catalogue actually has, so adding a language moves
                -- this number honestly instead of leaving it frozen.
                (SELECT count(*) FROM catalog.product p
                  WHERE (SELECT count(*) FROM catalog.product_tr tr WHERE tr.product_id = p.id)
                        < (SELECT n FROM locale_count))::int                       AS pending_translation,
                -- 'approval', NOT 'approved'. The domain's four states are
                -- draft, review, approval, published (WorkflowState), persisted
                -- lower-cased by ProductConfiguration. This read said 'approved',
                -- which no row can ever hold, so the KPI was structurally zero:
                -- a number that could not be wrong because it could not be
                -- anything. Caught by the smoke cross-check, not by any unit
                -- test, because nothing about a zero looks like a defect.
                (SELECT count(*) FROM product_completeness
                  WHERE workflow_state = @approvalState AND pct >= 90)::int        AS ready_to_publish
            """;

        // QuerySingleOrDefault, not QuerySingle: the gateway exposes the former.
        // The aggregate always returns exactly one row, so a null here would mean
        // the query itself changed shape — worth failing loudly rather than
        // rendering a cockpit full of zeroes that looks like an empty catalogue.
        return await DapperGateway.QuerySingleOrDefaultOnAsync<TotalsRow>(
                   connection, sql,
                   new { approvalState = DbState(WorkflowState.Approval) },
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   "Dashboard totals query returned no row; it must always return exactly one.");
    }

    private sealed record TotalsRow(
        int TotalProducts,
        int TotalSkus,
        int AvgCompleteness,
        int PendingTranslation,
        int ReadyToPublish);

    // ------------------------------------------------------- quality bands --

    /// <summary>
    /// v4.2's A/B/C/D distribution. The thresholds are the prototype's own
    /// labels made numeric; they are stated here rather than in the UI so the
    /// API and the screen cannot drift apart on what "Good" means.
    /// </summary>
    private static async Task<IReadOnlyList<QualityBandDto>> ReadQualityBandsAsync(
        NpgsqlConnection connection, int totalProducts, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH product_completeness AS (
                SELECT COALESCE(comp.pct, 0)::int AS pct
                FROM catalog.product p
                LEFT JOIN LATERAL (
                    SELECT round(100.0 * count(av.id) / nullif(count(sa.attribute_id), 0)) AS pct
                    FROM catalog.attribute_set_attribute sa
                    LEFT JOIN catalog.attribute_value av
                           ON av.product_id = p.id
                          AND av.attribute_id = sa.attribute_id
                          AND av.data_state = 'approved'
                          AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
                    WHERE sa.set_id = p.attribute_set_id AND sa.is_required
                ) comp ON true
            )
            SELECT band AS code, count(*)::int AS product_count
            FROM (
                SELECT CASE
                         WHEN pct >= 90 THEN 'A'
                         WHEN pct >= 75 THEN 'B'
                         WHEN pct >= 50 THEN 'C'
                         ELSE 'D'
                       END AS band
                FROM product_completeness
            ) b
            GROUP BY band
            """;

        var rows = await DapperGateway.QueryOnAsync<BandRow>(
            connection, sql, parameters: null, cancellationToken);

        var byCode = rows.ToDictionary(r => r.Code, r => r.ProductCount, StringComparer.Ordinal);

        // Every band is returned, including empty ones. A distribution that
        // silently omits its zero buckets makes a catalogue with no D-grade
        // products look identical to one where the D query broke.
        return
        [
            Band("A", "A — Excellent", byCode, totalProducts),
            Band("B", "B — Good", byCode, totalProducts),
            Band("C", "C — Needs work", byCode, totalProducts),
            Band("D", "D — Incomplete", byCode, totalProducts),
        ];
    }

    private static QualityBandDto Band(
        string code, string label, Dictionary<string, int> counts, int total)
    {
        var n = counts.GetValueOrDefault(code);
        return new QualityBandDto(code, label, n, total == 0 ? 0 : (int)Math.Round(100.0 * n / total));
    }

    private sealed record BandRow(string Code, int ProductCount);

    // -------------------------------------------------- completeness/areas --

    /// <summary>
    /// v4.2 shows five hand-written areas. This computes one row per attribute
    /// group that actually carries required attributes, so the panel follows the
    /// data model instead of a list somebody has to remember to update.
    ///
    /// Group names follow the reader (they are labels), with a base-locale
    /// fallback and the code as a last resort.
    /// </summary>
    private static async Task<IReadOnlyList<CompletenessByAreaDto>> ReadCompletenessByAreaAsync(
        NpgsqlConnection connection, DisplayLocale locale, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH base_locale AS (SELECT code FROM catalog.locale WHERE is_base LIMIT 1)
            SELECT ag.code                                                   AS group_code,
                   COALESCE(agtr.name, ag.code)                              AS label,
                   COALESCE(round(100.0 * count(av.id)
                                  / nullif(count(*), 0)), 0)::int            AS percent
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id AND sa.is_required
            JOIN catalog.attribute a  ON a.id = sa.attribute_id
            JOIN catalog.attribute_group ag ON ag.id = a.group_id
            LEFT JOIN LATERAL (
                SELECT gtr2.name
                FROM catalog.attribute_group_tr gtr2
                WHERE gtr2.group_id = ag.id
                  AND gtr2.locale_code IN (@displayLocale, (SELECT code FROM base_locale))
                ORDER BY (gtr2.locale_code = @displayLocale) DESC
                LIMIT 1
            ) agtr ON true
            LEFT JOIN catalog.attribute_value av
                   ON av.product_id = p.id
                  AND av.attribute_id = a.id
                  AND av.data_state = 'approved'
                  AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
            GROUP BY ag.code, COALESCE(agtr.name, ag.code), ag.sort_order
            ORDER BY ag.sort_order NULLS LAST, ag.code
            """;

        return await DapperGateway.QueryOnAsync<CompletenessByAreaDto>(
            connection, sql, new { displayLocale = locale.Code }, cancellationToken);
    }

    // ------------------------------------------------------------ pipeline --

    private static async Task<IReadOnlyList<WorkflowStageDto>> ReadWorkflowPipelineAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT workflow_state AS state, count(*)::int AS product_count
            FROM catalog.product
            GROUP BY workflow_state
            """;

        var rows = await DapperGateway.QueryOnAsync<PipelineRow>(
            connection, sql, parameters: null, cancellationToken);

        var byState = rows.ToDictionary(r => r.State, r => r.ProductCount, StringComparer.OrdinalIgnoreCase);

        // Stages come from the domain enum, not from strings typed here.
        //
        // The hand-written list this replaces had three entries — draft, review
        // and "approved" — against a four-state domain whose third state is
        // spelt "approval". So one stage counted nothing, one stage was missing
        // entirely, and the panel showed half the catalogue while looking
        // complete: 251 of 501 products, with no gap on screen to suggest it.
        //
        // Deriving the list means adding a state to WorkflowState adds it here,
        // and renaming one moves it here, instead of leaving a stage that
        // silently reports zero forever.
        // State codes are the enum names, matching what the products grid puts
        // in ProductListItemDto.WorkflowState. Two endpoints reporting the same
        // column in two different casings is a trap for anyone joining them, and
        // the grid's filter (?status=) parses either. The DATABASE lookup below
        // uses the lower-cased persisted form; only the wire contract is Pascal.
        var stages = new List<WorkflowStageDto>();
        foreach (var state in Enum.GetValues<WorkflowState>())
        {
            var key = DbState(state);
            stages.Add(new(state.ToString(), WorkflowLabel(state), byState.GetValueOrDefault(key)));
            byState.Remove(key);
        }

        // Anything left is a workflow_state the domain does not define — data
        // written by something other than this service, or a migration half
        // applied. Appended rather than dropped, because this panel is read as
        // a partition of the catalogue: a discarded row makes every other stage
        // look smaller than it is, and nothing on the screen would say so.
        foreach (var unknown in byState)
        {
            stages.Add(new(unknown.Key, $"{unknown.Key} (unrecognised)", unknown.Value));
        }

        return stages;
    }

    /// <summary>
    /// The persisted form of a workflow state. Mirrors ProductConfiguration's
    /// conversion (<c>state.ToString().ToLowerInvariant()</c>) — the one place
    /// that decides how these values are written.
    /// </summary>
    private static string DbState(WorkflowState state) =>
        state.ToString().ToLowerInvariant();

    /// <summary>
    /// Display text for a stage. English constants, like every other workflow
    /// label in the product: these are states of an editorial process, not
    /// catalogue content, and they are not in the translation tables. Rendering
    /// them through the locale would either invent translations or fall back
    /// silently to English while claiming to be localised.
    /// </summary>
    private static string WorkflowLabel(WorkflowState state) => state switch
    {
        WorkflowState.Draft => "Draft",
        WorkflowState.Review => "In review",
        WorkflowState.Approval => "In approval",
        WorkflowState.Published => "Published",
        _ => state.ToString(),
    };

    private sealed record PipelineRow(string State, int ProductCount);

    // ----------------------------------------------------- locale coverage --

    /// <summary>
    /// How far each configured locale has been translated. Not in v4.2 — it had
    /// two languages and a single "Pending Translation" count — but the
    /// catalogue now carries three, and a single number cannot say which of them
    /// is behind. This is the panel that makes the KPI actionable.
    /// </summary>
    private static async Task<IReadOnlyList<LocaleCoverageDto>> ReadLocaleCoverageAsync(
        NpgsqlConnection connection, int totalProducts, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.code,
                   l.name,
                   l.is_base,
                   count(tr.product_id)::int AS translated_products
            FROM catalog.locale l
            LEFT JOIN catalog.product_tr tr ON tr.locale_code = l.code
            GROUP BY l.code, l.name, l.is_base
            ORDER BY l.is_base DESC, l.code
            """;

        var rows = await DapperGateway.QueryOnAsync<LocaleRow>(
            connection, sql, parameters: null, cancellationToken);

        return rows
            .Select(r => new LocaleCoverageDto(
                r.Code,
                r.Name,
                r.IsBase,
                r.TranslatedProducts,
                totalProducts,
                totalProducts == 0 ? 0 : (int)Math.Round(100.0 * r.TranslatedProducts / totalProducts)))
            .ToList();
    }

    private sealed record LocaleRow(string Code, string Name, bool IsBase, int TranslatedProducts);

    // ------------------------------------------------------------ activity --

    /// <summary>
    /// Column order matches <see cref="DashboardActivityDto"/> exactly. R-17.
    ///
    /// Actor is null until authentication exists (R-3); the UI renders that as
    /// "Unknown" rather than inventing a name, which is decision 2026-08-16.
    /// </summary>
    private static async Task<IReadOnlyList<DashboardActivityDto>> ReadRecentActivityAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT al.occurred_at          AS occurred_at_utc,
                   al.actor_display        AS actor,
                   al.action,
                   p.product_code,
                   al.new_value->>'detail' AS detail
            FROM catalog.audit_log al
            JOIN catalog.product p
                 ON p.id = al.entity_id AND al.entity_type = 'product'
            ORDER BY al.occurred_at DESC, al.id DESC
            LIMIT @limit
            """;

        return await DapperGateway.QueryOnAsync<DashboardActivityDto>(
            connection, sql, new { limit = ActivityLimit }, cancellationToken);
    }
}
