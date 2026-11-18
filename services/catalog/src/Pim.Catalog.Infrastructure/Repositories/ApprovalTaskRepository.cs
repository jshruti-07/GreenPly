using Microsoft.EntityFrameworkCore;
using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Domain.Workflow;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.Repositories;

public sealed class ApprovalTaskRepository(CatalogDbContext dbContext) : IApprovalTaskRepository
{
    public Task<ApprovalTask?> GetOpenByProductIdAsync(long productId, CancellationToken cancellationToken) =>
        // History is owned by the aggregate, so EF loads it with the root — which
        // is what lets Perform() append to a collection that is actually complete.
        // ux_task_open_product guarantees at most one open row, so
        // FirstOrDefault cannot be hiding a second one.
        dbContext.ApprovalTasks
            .FirstOrDefaultAsync(
                t => t.ProductId == productId && t.Status == ApprovalTaskStatus.Open,
                cancellationToken);

    public void Add(ApprovalTask task) => dbContext.ApprovalTasks.Add(task);
}
