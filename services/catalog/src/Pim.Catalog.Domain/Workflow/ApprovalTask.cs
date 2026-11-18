using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Workflow;

public enum ApprovalTaskStatus
{
    Open,
    Completed,
    Cancelled
}

public enum ApprovalPriority
{
    Low,
    Normal,
    High,
    Urgent
}

/// <summary>
/// One immutable row of the approval trail (approval_history). Appended, never
/// edited, never removed.
/// </summary>
public sealed class ApprovalHistoryEntry : Entity
{
    private ApprovalHistoryEntry()
    {
        // EF Core materialization only.
    }

    internal ApprovalHistoryEntry(
        long? fromStepId,
        long toStepId,
        WorkflowAction action,
        string? actorDisplay,
        string? comment,
        DateTimeOffset occurredAt)
    {
        FromStepId = fromStepId;
        ToStepId = toStepId;
        Action = action;
        ActorDisplay = actorDisplay;
        Comment = comment;
        OccurredAt = occurredAt;
    }

    public long TaskId { get; private set; }

    /// <summary>Null on the row that opened the task: there was nowhere to come from.</summary>
    public long? FromStepId { get; private set; }

    public long ToStepId { get; private set; }

    public WorkflowAction Action { get; private set; }

    /// <summary>
    /// Always null today. There is no identity service (R-3), so nothing can
    /// populate it, and filling it with a guess would be worse than leaving the
    /// trail honestly anonymous (R-26).
    /// </summary>
    public long? ActorUserId { get; private set; }

    /// <summary>Whatever the caller claimed to be. Unverified — see R-26.</summary>
    public string? ActorDisplay { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// A product's live position in the editorial pipeline (approval_task).
///
/// This is an aggregate root and NOT part of the Product aggregate, but the two
/// are saved together in one transaction on purpose: product.workflow_state is
/// a denormalised projection of this task's current step, and a transaction
/// that updated one without the other would leave the board and the grid
/// disagreeing about where a product is. That is why the task and its history
/// are mapped with EF Core rather than written through a Dapper store like the
/// read models are — the unit of work is the whole point.
/// </summary>
public sealed class ApprovalTask : AggregateRoot
{
    private readonly List<ApprovalHistoryEntry> _history = [];

    private ApprovalTask()
    {
        // EF Core materialization only.
    }

    public Guid PublicId { get; private set; }

    public long ProductId { get; private set; }

    public long DefinitionId { get; private set; }

    public long CurrentStepId { get; private set; }

    public ApprovalTaskStatus Status { get; private set; }

    public ApprovalPriority Priority { get; private set; }

    public DateOnly? DueDate { get; private set; }

    /// <summary>Null until authentication ships; see <see cref="AssigneeDisplayName"/>.</summary>
    public long? AssigneeUserId { get; private set; }

    /// <summary>
    /// Interim assignee as display text, the same compromise
    /// product.owner_display_name makes (decision 2026-08-14). Assignment by
    /// ROLE is deliberately not implemented: with no users and no roles, a role
    /// check would pass for everyone, and a check that cannot fail is not a
    /// check.
    /// </summary>
    public string? AssigneeDisplayName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ApprovalHistoryEntry> History => _history.AsReadOnly();

    /// <summary>
    /// Opens a task at the step the product is ALREADY in, rather than at the
    /// pipeline's starting step.
    ///
    /// That distinction matters for the 500 products that predate this module:
    /// they carry a workflow_state and no task, and forcing them back to Draft
    /// to acquire one would rewrite the catalogue's governance state as a side
    /// effect of deploying a feature.
    /// </summary>
    public static ApprovalTask OpenAt(
        long productId,
        WorkflowDefinition definition,
        WorkflowStepDefinition step,
        string? assigneeDisplayName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(step);

        return new ApprovalTask
        {
            PublicId = Guid.NewGuid(),
            ProductId = productId,
            DefinitionId = definition.Id,
            CurrentStepId = step.Id,
            Status = ApprovalTaskStatus.Open,
            Priority = ApprovalPriority.Normal,
            AssigneeDisplayName = string.IsNullOrWhiteSpace(assigneeDisplayName)
                ? null
                : assigneeDisplayName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Applies an action and records it. Returns the step the task landed on so
    /// the caller can project it onto product.workflow_state.
    /// </summary>
    public Result<WorkflowStepDefinition> Perform(
        WorkflowDefinition definition,
        WorkflowAction action,
        string? comment,
        string? actorDisplay,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (Status != ApprovalTaskStatus.Open)
        {
            return Result.Failure<WorkflowStepDefinition>(WorkflowErrors.TaskAlreadyClosed);
        }

        var transition = definition.Resolve(CurrentStepId, action);

        if (transition is null)
        {
            var current = definition.StepById(CurrentStepId);
            return Result.Failure<WorkflowStepDefinition>(
                WorkflowErrors.ActionNotAvailable(
                    WorkflowActions.ToToken(action), current?.Name ?? string.Empty));
        }

        var target = definition.StepById(transition.ToStepId);

        if (target is null)
        {
            // Create() rejects dangling transitions, so reaching here means the
            // definition was mutated after construction, which it cannot be.
            throw new InvalidWorkflowDefinitionException(
                $"Workflow definition '{definition.Code}' resolved an action to a step it does not contain.");
        }

        _history.Add(new ApprovalHistoryEntry(
            CurrentStepId,
            target.Id,
            action,
            string.IsNullOrWhiteSpace(actorDisplay) ? null : actorDisplay.Trim(),
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            now));

        CurrentStepId = target.Id;
        UpdatedAt = now;

        // A terminal step has nothing to do next, so the task stops being work.
        // It is completed rather than deleted: the history hangs off it.
        if (target.IsTerminal)
        {
            Status = ApprovalTaskStatus.Completed;
        }

        return Result.Success(target);
    }

    /// <summary>
    /// Forces the task onto a step without recording a transition.
    ///
    /// This is a REPAIR, not a move. It exists for one case: something outside
    /// this service changed product.workflow_state directly — a bulk import, a
    /// migration, a DBA — leaving the projection and the task disagreeing. The
    /// product's state wins, because it is what every screen, filter and export
    /// in the system reads.
    ///
    /// No history row is written, deliberately: nobody performed an action, and
    /// inventing one would put a transition in an approval trail that never
    /// happened. The caller logs it instead, and scripts/smoke-api.sh asserts
    /// the two never disagree in the first place.
    /// </summary>
    public void RealignTo(WorkflowStepDefinition step, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(step);

        CurrentStepId = step.Id;
        UpdatedAt = now;
    }

    public void Assign(string? displayName, DateTimeOffset now)
    {
        AssigneeDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAt = now;
    }
}
