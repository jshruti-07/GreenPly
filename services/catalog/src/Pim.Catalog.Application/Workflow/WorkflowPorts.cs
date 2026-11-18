using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Workflow;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// The active pipeline, cached for the process.
///
/// Named a "provider interface" in PIM-Project-Technology-Stack-Matrix v1.0,
/// which lists Elsa, Temporal, Logic Apps, Durable Functions and Step Functions
/// as things that could sit behind it on a given deployment. The shipped
/// implementation is neither: it is an in-process state machine over the
/// workflow_* tables, per the client's instruction not to take a third-party
/// workflow dependency (2026-08-19). Should that ever change, the replacement
/// implements this interface and the rest of the module does not move.
/// </summary>
public interface IWorkflowCatalog
{
    /// <summary>
    /// Throws <see cref="InvalidWorkflowDefinitionException"/> if the deployment
    /// has no active pipeline for the entity type, or has one that does not
    /// describe a usable process. That is not a Result: a PIM with no editorial
    /// process is not serving a bad request, it is misconfigured.
    /// </summary>
    Task<WorkflowDefinition> GetActiveAsync(string entityType, CancellationToken cancellationToken);
}

/// <summary>Write-side persistence for approval tasks. EF-mapped; see ApprovalTask for why.</summary>
public interface IApprovalTaskRepository
{
    Task<ApprovalTask?> GetOpenByProductIdAsync(long productId, CancellationToken cancellationToken);

    void Add(ApprovalTask task);
}

/// <summary>
/// One product's workflow row as the database holds it. Positional record —
/// the SELECT's column order must match these parameters exactly, because
/// Dapper materialises positional records BY POSITION and a reordered column
/// list produces plausible-looking nonsense rather than an error (R-17).
/// </summary>
public sealed record ProductWorkflowRow(
    long ProductId,
    string ProductCode,
    string WorkflowState,
    int RowVersion,
    string? OwnerDisplayName,
    long? TaskId,
    string? TaskStatus,
    string? Priority,
    DateOnly? DueDate,
    string? AssigneeDisplayName,
    string? TaskStepCode);

/// <summary>One row of the approval trail, joined to step codes. Positional — see R-17.</summary>
public sealed record ProductWorkflowHistoryRow(
    string Action,
    string? FromStepCode,
    string ToStepCode,
    string? ActorDisplay,
    string? Comment,
    DateTimeOffset OccurredAt);

public interface IWorkflowReadStore
{
    Task<ProductWorkflowRow?> GetProductWorkflowAsync(string productCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductWorkflowHistoryRow>> GetHistoryAsync(long taskId, CancellationToken cancellationToken);
}

/// <summary>How many products stand in one workflow state, across the whole catalogue.</summary>
public sealed record WorkflowStateTotal(string WorkflowState, int Total);

/// <summary>
/// A board card as the database returns it. Positional record — the SELECT's
/// column order and these parameters are one coupled unit (R-17).
/// </summary>
public sealed record WorkflowCardRow(
    Guid PublicId,
    string Code,
    string Name,
    string Category,
    int CompletenessPct,
    string WorkflowState,
    string? OwnerDisplayName,
    string? AssigneeDisplayName,
    string? Priority,
    DateOnly? DueDate,
    DateTimeOffset UpdatedAt,
    bool HasOpenTask);

/// <summary>One list-view row. Positional — see R-17.</summary>
public sealed record WorkflowQueueRow(
    Guid PublicId,
    string Code,
    string Name,
    string Category,
    int CompletenessPct,
    string WorkflowState,
    string? OwnerDisplayName,
    string? AssigneeDisplayName,
    string? Priority,
    DateOnly? DueDate,
    DateTimeOffset UpdatedAt,
    string? LastAction,
    DateTimeOffset? LastActionAt);

public sealed record WorkflowQueueFilter(
    int Page,
    int PageSize,
    string? Step,
    string? Assignee,
    string? Priority,
    string? Search,
    string SortBy,
    bool SortDescending,
    DisplayLocale DisplayLocale);

public interface IWorkflowBoardStore
{
    /// <summary>
    /// Every state present in the catalogue with its count — including states
    /// the active pipeline does not define, because a column silently dropped
    /// makes every other column a larger share of a catalogue that shrank.
    /// </summary>
    Task<IReadOnlyList<WorkflowStateTotal>> GetStateTotalsAsync(CancellationToken cancellationToken);

    /// <summary>The top <paramref name="cardsPerColumn"/> most recently touched products per state.</summary>
    Task<IReadOnlyList<WorkflowCardRow>> GetCardsAsync(
        int cardsPerColumn, DisplayLocale displayLocale, CancellationToken cancellationToken);

    Task<PagedResult<WorkflowQueueRow>> GetQueueAsync(
        WorkflowQueueFilter filter, CancellationToken cancellationToken);

    /// <summary>Distinct assignees on open tasks, for the list view's filter.</summary>
    Task<IReadOnlyList<string>> GetAssigneesAsync(CancellationToken cancellationToken);
}
