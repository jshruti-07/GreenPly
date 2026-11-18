using System.Collections.Concurrent;
using Npgsql;
using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.Catalog.Infrastructure.ReadModels;

namespace Pim.Catalog.Infrastructure.Workflow;

/// <summary>
/// The active pipeline, read once and cached — the same shape as
/// <see cref="LocaleCatalog"/>, and for the same reason: it is consulted on
/// every workflow read and every workflow write, over three small tables that
/// change when somebody redesigns the editorial process.
///
/// The cache has a lifetime rather than being loaded at startup. A definition
/// changed by hand should take effect without a deployment, and a few minutes
/// of staleness on "what the pipeline is" cannot corrupt anything: the database
/// constraints still hold, and the worst case is a button that appears a few
/// minutes late. There is deliberately no invalidation hook, because nothing in
/// this service writes to workflow_definition yet and a hook with no caller is
/// dead code that reads as a guarantee.
/// </summary>
public sealed class WorkflowCatalog(NpgsqlDataSource dataSource) : IWorkflowCatalog, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private const string DefinitionSql = """
        SELECT id, code, name, entity_type, version
        FROM catalog.workflow_definition
        WHERE entity_type = @entityType AND is_active;
        """;

    private const string StepSql = """
        SELECT id, code, name, sort_order, owner_role, is_initial, is_terminal
        FROM catalog.workflow_step
        WHERE definition_id = @definitionId
        ORDER BY sort_order, id;
        """;

    private const string TransitionSql = """
        SELECT id, from_step_id, to_step_id, action, label, sort_order
        FROM catalog.workflow_transition
        WHERE definition_id = @definitionId
        ORDER BY from_step_id, sort_order, id;
        """;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, (WorkflowDefinition Definition, DateTimeOffset LoadedAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public async Task<WorkflowDefinition> GetActiveAsync(string entityType, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        if (_cache.TryGetValue(entityType, out var cached)
            && DateTimeOffset.UtcNow - cached.LoadedAt < CacheLifetime)
        {
            return cached.Definition;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock: several requests arrive together on an
            // expired entry and only the first should pay for three round trips.
            if (_cache.TryGetValue(entityType, out cached)
                && DateTimeOffset.UtcNow - cached.LoadedAt < CacheLifetime)
            {
                return cached.Definition;
            }

            var definition = await LoadAsync(entityType, cancellationToken);
            _cache[entityType] = (definition, DateTimeOffset.UtcNow);

            return definition;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<WorkflowDefinition> LoadAsync(string entityType, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var definitions = await DapperGateway.QueryOnAsync<DefinitionRow>(
            connection, DefinitionSql, new { entityType }, cancellationToken);

        if (definitions.Count == 0)
        {
            // Not a Result. A deployment with no editorial process cannot serve
            // the workflow module at all, and the caller cannot fix it by
            // sending something different.
            throw new InvalidWorkflowDefinitionException(
                $"No active workflow definition exists for entity type '{entityType}'. "
                + "The catalog schema seeds one; check that db/init ran to completion.");
        }

        // ux_workflow_active makes a second active row impossible, so this is
        // belt and braces — but a silently-chosen definition is exactly the kind
        // of thing that is discovered months later, so it says so out loud.
        if (definitions.Count > 1)
        {
            throw new InvalidWorkflowDefinitionException(
                $"{definitions.Count} active workflow definitions exist for entity type '{entityType}'. "
                + "Exactly one must be active.");
        }

        var definition = definitions[0];

        var steps = await DapperGateway.QueryOnAsync<StepRow>(
            connection, StepSql, new { definitionId = definition.Id }, cancellationToken);

        var transitions = await DapperGateway.QueryOnAsync<TransitionRow>(
            connection, TransitionSql, new { definitionId = definition.Id }, cancellationToken);

        return WorkflowDefinition.Create(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.EntityType,
            definition.Version,
            steps.Select(ToStep).ToArray(),
            transitions.Select(ToTransition).ToArray());
    }

    private static WorkflowStepDefinition ToStep(StepRow row)
    {
        if (!Enum.TryParse<WorkflowState>(row.Code, ignoreCase: true, out var state))
        {
            // product.workflow_state is a CHECK-constrained text column and the
            // domain reads it as an enum. A step whose code is not one of those
            // values could never be projected onto a product, so the pipeline
            // would half-work: the step would exist and no product could stand
            // in it.
            throw new InvalidWorkflowDefinitionException(
                $"Workflow step '{row.Code}' does not correspond to a product workflow state. "
                + $"Valid step codes: {string.Join(", ", Enum.GetNames<WorkflowState>().Select(n => n.ToLowerInvariant()))}.");
        }

        return new WorkflowStepDefinition(
            row.Id, row.Code, row.Name, row.SortOrder, row.OwnerRole,
            row.IsInitial, row.IsTerminal, state);
    }

    private static WorkflowTransitionDefinition ToTransition(TransitionRow row)
    {
        if (!WorkflowActions.TryParse(row.Action, out var action))
        {
            throw new InvalidWorkflowDefinitionException(
                $"Workflow transition {row.Id} uses action '{row.Action}', which this build does not implement.");
        }

        return new WorkflowTransitionDefinition(
            row.Id, row.FromStepId, row.ToStepId, action, row.Label, row.SortOrder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshLock.Dispose();
    }

    // Positional records: the SELECT column order above must match these
    // parameters exactly. Dapper materialises positional records BY POSITION,
    // so a reordered column list produces a plausible object rather than an
    // error (R-17).
    private sealed record DefinitionRow(long Id, string Code, string Name, string EntityType, int Version);

    private sealed record StepRow(
        long Id, string Code, string Name, int SortOrder, string? OwnerRole, bool IsInitial, bool IsTerminal);

    private sealed record TransitionRow(
        long Id, long FromStepId, long ToStepId, string Action, string Label, int SortOrder);
}
