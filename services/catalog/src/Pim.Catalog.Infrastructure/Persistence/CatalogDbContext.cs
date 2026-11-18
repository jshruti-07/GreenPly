using MediatR;
using Microsoft.EntityFrameworkCore;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.Catalog.Infrastructure.Outbox;
using Pim.SharedKernel.Domain;

namespace Pim.Catalog.Infrastructure.Persistence;

/// <summary>
/// Catalog service DbContext. Owns only catalog tables; other services get
/// their own contexts against their own schema (ADR-005).
///
/// Domain events leave here two ways, and the difference matters:
///
///   - As outbox rows, written INSIDE this save's transaction. That is what
///     ADR-004 requires and what Sprint 15 added. If the commit rolls back the
///     messages roll back with it, so the catalogue and the world cannot
///     disagree about whether something happened.
///
///   - As in-process MediatR notifications, published AFTER the commit. These
///     are best-effort and always were: a handler that fails here leaves a
///     committed change undelivered. Nothing consumes them today. Anything that
///     must not be lost belongs in the outbox, not here.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IPublisher publisher)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Approval tasks are written through EF and not Dapper so that a task
    /// move, its history row and the projection on product.workflow_state share
    /// one transaction. See ApprovalTaskConfiguration.
    /// </summary>
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }

    /// <summary>
    /// Blocked on purpose. The synchronous path would commit aggregate changes
    /// without ever running the enqueue below, and the resulting bug — events
    /// that simply never exist — is invisible at the call site. There are no
    /// synchronous callers today; this makes sure a future one fails loudly
    /// instead of silently dropping messages.
    /// </summary>
    public override int SaveChanges() =>
        throw new NotSupportedException(
            "Use SaveChangesAsync. The synchronous path bypasses outbox enqueue (ADR-007).");

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        // Enqueue BEFORE saving. These inserts then belong to the same
        // transaction EF opens for this SaveChanges, which is the entire
        // guarantee: no committed change without its message, no message
        // without its committed change.
        //
        // One timestamp for the whole batch, taken once, so messages written by
        // a single business operation share an enqueue time and sort stably.
        if (aggregates.Count > 0)
        {
            var enqueuedAt = TimeProvider.System.GetUtcNow();

            foreach (var aggregate in aggregates)
            {
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    // CorrelationId is null until request correlation exists.
                    // It is not invented here: a fabricated correlation id is
                    // worse than none, because it looks like evidence.
                    OutboxMessages.Add(
                        IntegrationEventMapper.ToOutboxMessage(domainEvent, enqueuedAt, correlationId: null));
                }
            }
        }

        var written = await base.SaveChangesAsync(cancellationToken);

        // Post-commit, in-process, best-effort. See the class remarks.
        foreach (var aggregate in aggregates)
        {
            var events = aggregate.DomainEvents.ToList();
            aggregate.ClearDomainEvents();

            foreach (var domainEvent in events)
            {
                await publisher.Publish(domainEvent, cancellationToken);
            }
        }

        return written;
    }
}
