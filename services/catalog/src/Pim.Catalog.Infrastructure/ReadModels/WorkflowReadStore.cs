using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Reads for the Workflow tab. Dapper, on the request's own connection, like
/// every other read model — the write side of this module is the EF-mapped
/// ApprovalTask aggregate and the two do not share a path on purpose.
/// </summary>
public sealed class WorkflowReadStore(CatalogDbContext dbContext) : IWorkflowReadStore
{
    /// <summary>
    /// The product's position, plus its most relevant task.
    ///
    /// "Most relevant" is the open one if there is an open one, and otherwise
    /// the most recently touched. Selecting only open tasks would be the
    /// obvious query and would be wrong: a published product's task is
    /// COMPLETED, and hiding it would empty the approval trail at exactly the
    /// moment the trail is worth reading.
    ///
    /// Columns are aliased even where the alias matches, because two of them
    /// are `code` from two joins to the same table.
    /// </summary>
    private const string ProductWorkflowSql = """
        SELECT p.id                      AS product_id,
               p.product_code            AS product_code,
               p.workflow_state          AS workflow_state,
               p.row_version             AS row_version,
               p.owner_display_name      AS owner_display_name,
               t.id                      AS task_id,
               t.status                  AS task_status,
               t.priority                AS priority,
               t.due_date                AS due_date,
               t.assignee_display_name   AS assignee_display_name,
               ws.code                   AS task_step_code
        FROM catalog.product p
        LEFT JOIN LATERAL (
            SELECT at.id, at.status, at.priority, at.due_date,
                   at.assignee_display_name, at.current_step_id
            FROM catalog.approval_task at
            WHERE at.product_id = p.id
            ORDER BY (at.status = 'open') DESC, at.updated_at DESC, at.id DESC
            LIMIT 1
        ) t ON true
        LEFT JOIN catalog.workflow_step ws ON ws.id = t.current_step_id
        WHERE p.product_code = @code;
        """;

    private const string HistorySql = """
        SELECT h.action        AS action,
               fs.code         AS from_step_code,
               ts.code         AS to_step_code,
               h.actor_display AS actor_display,
               h.comment       AS comment,
               h.occurred_at   AS occurred_at
        FROM catalog.approval_history h
        LEFT JOIN catalog.workflow_step fs ON fs.id = h.from_step_id
        JOIN catalog.workflow_step ts ON ts.id = h.to_step_id
        WHERE h.task_id = @taskId
        ORDER BY h.occurred_at, h.id;
        """;

    public Task<ProductWorkflowRow?> GetProductWorkflowAsync(
        string productCode, CancellationToken cancellationToken) =>
        DapperGateway.QuerySingleOrDefaultAsync<ProductWorkflowRow>(
            dbContext, ProductWorkflowSql, new { code = productCode }, cancellationToken);

    public Task<IReadOnlyList<ProductWorkflowHistoryRow>> GetHistoryAsync(
        long taskId, CancellationToken cancellationToken) =>
        DapperGateway.QueryAsync<ProductWorkflowHistoryRow>(
            dbContext, HistorySql, new { taskId }, cancellationToken);
}
