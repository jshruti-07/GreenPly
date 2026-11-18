using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Reads behind the Workflows &amp; Tasks screen.
///
/// The board is over PRODUCTS, not over approval tasks, and that is the whole
/// design. Most of the catalogue has never been through the pipeline: a board
/// built from `approval_task` would show the handful of products somebody has
/// touched and call it "the editorial pipeline across the catalogue", which is
/// what the screen's own subtitle promises not to do. Products carry
/// `workflow_state`; tasks add an assignee, a priority and a due date when one
/// exists.
///
/// The locale laterals below are written out rather than shared. Twelve read
/// models in this folder each carry their own copy of the same three-line
/// fallback join; consolidating them is a worthwhile refactor and a different
/// change from this one, so this file follows the house convention instead of
/// half-leaving it.
/// </summary>
public sealed class WorkflowBoardStore(CatalogDbContext dbContext) : IWorkflowBoardStore
{
    /// <summary>
    /// Sortable columns for the list view. The ONLY strings interpolated into
    /// the command text; every filter value travels as a parameter.
    ///
    /// Priority is deliberately absent. Its four values sort alphabetically as
    /// high, low, normal, urgent — which is not the order anybody means, and a
    /// sort that silently reorders by the wrong scale is worse than no sort.
    /// A CASE expression would fix it and belongs with the decision to make
    /// priority mean something, which nothing yet does.
    /// </summary>
    private static readonly FrozenDictionary<string, string> SortExpressions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "p.product_code",
            ["name"] = "COALESCE(tr.name, p.product_code)",
            ["category"] = "COALESCE(ntr.name, tn.code)",
            ["step"] = "p.workflow_state",
            ["owner"] = "p.owner_display_name",
            ["assignee"] = "t.assignee_display_name",
            ["due"] = "t.due_date",
            ["updated"] = "p.updated_at",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SortableColumns { get; } = ["code", "name", "category", "step", "owner", "assignee", "due", "updated"];

    private const string NameLateral = """
        LEFT JOIN LATERAL (
            SELECT ptr.name
            FROM catalog.product_tr ptr
            WHERE ptr.product_id = p.id
              AND ptr.locale_code IN (@displayLocale, p.base_locale)
            ORDER BY (ptr.locale_code = @displayLocale) DESC
            LIMIT 1
        ) tr ON true
        """;

    private const string CategoryLateral = """
        LEFT JOIN LATERAL (
            SELECT tntr.name
            FROM catalog.taxonomy_node_tr tntr
            WHERE tntr.node_id = tn.id
              AND tntr.locale_code IN (@displayLocale, p.base_locale)
            ORDER BY (tntr.locale_code = @displayLocale) DESC
            LIMIT 1
        ) ntr ON true
        """;

    /// <summary>
    /// The same completeness rule the grid and the cockpit use: approved values
    /// for required attributes, base locale or language-neutral. Not
    /// @displayLocale — whether a product is finished is a property of the
    /// product, not of who is reading it.
    /// </summary>
    private const string CompletenessLateral = """
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
        """;

    private const string OpenTaskJoin = """
        LEFT JOIN catalog.approval_task t
               ON t.product_id = p.id AND t.status = 'open'
        """;

    private const string StateTotalsSql = """
        SELECT lower(p.workflow_state) AS workflow_state, count(*)::int AS total
        FROM catalog.product p
        GROUP BY lower(p.workflow_state)
        ORDER BY lower(p.workflow_state);
        """;

    private const string AssigneesSql = """
        SELECT DISTINCT t.assignee_display_name
        FROM catalog.approval_task t
        WHERE t.status = 'open' AND t.assignee_display_name IS NOT NULL
        ORDER BY t.assignee_display_name;
        """;

    public Task<IReadOnlyList<WorkflowStateTotal>> GetStateTotalsAsync(CancellationToken cancellationToken) =>
        DapperGateway.QueryAsync<WorkflowStateTotal>(dbContext, StateTotalsSql, parameters: null, cancellationToken);

    public Task<IReadOnlyList<string>> GetAssigneesAsync(CancellationToken cancellationToken) =>
        DapperGateway.QueryAsync<string>(dbContext, AssigneesSql, parameters: null, cancellationToken);

    /// <summary>
    /// One statement for the whole board: rank within each state, keep the top
    /// n, then compute the derived columns for those rows only. The alternative
    /// — one query per column — would be four round trips whose results came
    /// from four different moments.
    /// </summary>
    public Task<IReadOnlyList<WorkflowCardRow>> GetCardsAsync(
        int cardsPerColumn, DisplayLocale displayLocale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(displayLocale);
        ArgumentOutOfRangeException.ThrowIfLessThan(cardsPerColumn, 1);

        var sql = $"""
            WITH ranked AS (
                SELECT p.id,
                       row_number() OVER (
                           PARTITION BY p.workflow_state
                           ORDER BY p.updated_at DESC, p.id DESC) AS rn
                FROM catalog.product p
            )
            SELECT p.public_id                    AS public_id,
                   p.product_code                 AS code,
                   COALESCE(tr.name, p.product_code) AS name,
                   COALESCE(ntr.name, tn.code)    AS category,
                   COALESCE(comp.pct, 0)::int     AS completeness_pct,
                   p.workflow_state               AS workflow_state,
                   p.owner_display_name           AS owner_display_name,
                   t.assignee_display_name        AS assignee_display_name,
                   t.priority                     AS priority,
                   t.due_date                     AS due_date,
                   p.updated_at                   AS updated_at,
                   (t.id IS NOT NULL)             AS has_open_task
            FROM ranked r
            JOIN catalog.product p        ON p.id = r.id
            LEFT JOIN catalog.taxonomy_node tn ON tn.id = p.primary_node_id
            {NameLateral}
            {CategoryLateral}
            {CompletenessLateral}
            {OpenTaskJoin}
            WHERE r.rn <= @cardsPerColumn
            ORDER BY p.workflow_state, p.updated_at DESC, p.id DESC;
            """;

        return DapperGateway.QueryAsync<WorkflowCardRow>(
            dbContext, sql,
            new { cardsPerColumn, displayLocale = displayLocale.Code },
            cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "ORDER BY comes from a fixed whitelist; every filter value is a Dapper parameter.")]
    public async Task<PagedResult<WorkflowQueueRow>> GetQueueAsync(
        WorkflowQueueFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!SortExpressions.TryGetValue(filter.SortBy, out var sort))
        {
            throw new ArgumentException($"Unwhitelisted sort key '{filter.SortBy}'.", nameof(filter));
        }

        var direction = filter.SortDescending ? "DESC" : "ASC";

        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("displayLocale", filter.DisplayLocale.Code);

        if (!string.IsNullOrWhiteSpace(filter.Step))
        {
            conditions.Add("p.workflow_state = @step");
            parameters.Add("step", filter.Step.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(filter.Assignee))
        {
            conditions.Add("t.assignee_display_name = @assignee");
            parameters.Add("assignee", filter.Assignee.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            conditions.Add("t.priority = @priority");
            parameters.Add("priority", filter.Priority.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Substring, over the code and over EVERY translation rather than
            // the displayed one: matching is locale-agnostic, rendering is not
            // (the rule Sprint 17 had to write down after breaking it).
            conditions.Add("""
                (p.product_code ILIKE @search
                 OR EXISTS (SELECT 1 FROM catalog.product_tr s
                             WHERE s.product_id = p.id AND s.name ILIKE @search))
                """);
            parameters.Add("search", $"%{filter.Search.Trim()}%");
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join("\n              AND ", conditions);

        var countSql = $"""
            SELECT count(*)::int
            FROM catalog.product p
            JOIN catalog.taxonomy_node tn ON tn.id = p.primary_node_id
            {OpenTaskJoin}
            {where};
            """;

        var total = await DapperGateway.QuerySingleOrDefaultAsync<int>(
            dbContext, countSql, parameters, cancellationToken);

        var pageSql = $"""
            SELECT p.public_id                    AS public_id,
                   p.product_code                 AS code,
                   COALESCE(tr.name, p.product_code) AS name,
                   COALESCE(ntr.name, tn.code)    AS category,
                   COALESCE(comp.pct, 0)::int     AS completeness_pct,
                   p.workflow_state               AS workflow_state,
                   p.owner_display_name           AS owner_display_name,
                   t.assignee_display_name        AS assignee_display_name,
                   t.priority                     AS priority,
                   t.due_date                     AS due_date,
                   p.updated_at                   AS updated_at,
                   last.action                    AS last_action,
                   last.occurred_at               AS last_action_at
            FROM catalog.product p
            JOIN catalog.taxonomy_node tn ON tn.id = p.primary_node_id
            {NameLateral}
            {CategoryLateral}
            {CompletenessLateral}
            {OpenTaskJoin}
            LEFT JOIN LATERAL (
                SELECT h.action, h.occurred_at
                FROM catalog.approval_history h
                JOIN catalog.approval_task at2 ON at2.id = h.task_id
                WHERE at2.product_id = p.id
                ORDER BY h.occurred_at DESC, h.id DESC
                LIMIT 1
            ) last ON true
            {where}
            ORDER BY {sort} {direction} NULLS LAST, p.id ASC
            LIMIT @pageSize OFFSET @offset;
            """;

        parameters.Add("pageSize", filter.PageSize);
        parameters.Add("offset", (long)(filter.Page - 1) * filter.PageSize);

        var rows = await DapperGateway.QueryAsync<WorkflowQueueRow>(
            dbContext, pageSql, parameters, cancellationToken);

        return new PagedResult<WorkflowQueueRow>(rows, filter.Page, filter.PageSize, total);
    }
}
