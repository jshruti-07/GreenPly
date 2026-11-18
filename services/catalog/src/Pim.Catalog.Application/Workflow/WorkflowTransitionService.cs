using Microsoft.Extensions.Logging;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// The one path a product takes through the pipeline.
///
/// Both workflow commands go through here — the action-oriented one the
/// Workflow tab posts (APPROVE, REJECT, …) and the state-oriented one the
/// detail header has posted since Sprint 4 ("Move to Review"). Two entry points
/// with two copies of the rules is how the transition table ended up written
/// three times in the first place, so there is exactly one copy of the sequence
/// below and both commands call it.
///
/// It does NOT save. The caller owns the unit of work, because the point of
/// mapping the task with EF Core is that the task move, its history row and
/// product.workflow_state land in one transaction.
/// </summary>
public sealed partial class WorkflowTransitionService(
    IWorkflowCatalog catalog,
    IApprovalTaskRepository tasks,
    TimeProvider clock,
    ILogger<WorkflowTransitionService> logger)
{
    public const string ProductEntityType = "PRODUCT";

    public Task<WorkflowDefinition> GetProductPipelineAsync(CancellationToken cancellationToken) =>
        catalog.GetActiveAsync(ProductEntityType, cancellationToken);

    /// <summary>Applies an action by name, so the API layer never parses domain vocabulary.</summary>
    public async Task<Result<WorkflowStepDefinition>> PerformAsync(
        Product product,
        string action,
        string? comment,
        string? actorDisplay,
        CancellationToken cancellationToken)
    {
        if (!WorkflowActions.TryParse(action, out var parsed))
        {
            return Result.Failure<WorkflowStepDefinition>(WorkflowErrors.UnknownAction(action));
        }

        return await PerformAsync(product, parsed, comment, actorDisplay, cancellationToken);
    }

    public async Task<Result<WorkflowStepDefinition>> PerformAsync(
        Product product,
        WorkflowAction action,
        string? comment,
        string? actorDisplay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        var definition = await catalog.GetActiveAsync(ProductEntityType, cancellationToken);
        var current = definition.StepFor(product.WorkflowState);

        if (current is null)
        {
            return Result.Failure<WorkflowStepDefinition>(
                WorkflowErrors.StepNotInPipeline(product.WorkflowState.ToString()));
        }

        var task = await tasks.GetOpenByProductIdAsync(product.Id, cancellationToken);
        var now = clock.GetUtcNow();

        if (task is null)
        {
            // The catalogue predates this module: 500 products carry a workflow
            // state and no task. Opening the task WHERE THE PRODUCT ALREADY IS,
            // rather than at the pipeline's start, means deploying the feature
            // does not quietly reset the governance state of the catalogue.
            task = ApprovalTask.OpenAt(product.Id, definition, current, product.OwnerDisplayName, now);
            tasks.Add(task);
            LogTaskOpened(logger, product.Code.Value, current.Code);
        }
        else if (task.CurrentStepId != current.Id)
        {
            // Should be impossible: the two are written in one transaction. If
            // it happens, something outside this service moved the product —
            // and the product's state wins, because it is what every screen and
            // every export reads. Logged rather than swallowed; the smoke suite
            // fails if the two ever disagree.
            LogProjectionDrift(
                logger, product.Code.Value, task.CurrentStepId, current.Code, current.Id);
            task.RealignTo(current, now);
        }

        var moved = task.Perform(definition, action, comment, actorDisplay, now);

        if (moved.IsFailure)
        {
            return moved;
        }

        // The projection. If the aggregate refuses the move the task has already
        // recorded it, which is why this cannot be allowed to fail silently —
        // the caller must not save. Both use the SAME definition object, so the
        // only way to get here is a product state outside the pipeline, already
        // handled above.
        var applied = product.ChangeWorkflowState(moved.Value.State, definition);

        if (applied.IsFailure)
        {
            return Result.Failure<WorkflowStepDefinition>(applied.Error);
        }

        LogTransition(logger, product.Code.Value, current.Code, moved.Value.Code, action);

        return moved;
    }

    /// <summary>
    /// Applies a move expressed as a TARGET STATE rather than an action, for the
    /// detail header's "Move to Review" buttons and the endpoint they have
    /// posted to since Sprint 4.
    ///
    /// Where two actions reach the same step -- REJECT and REQUEST_CHANGES both
    /// land on Draft -- this takes the pipeline's own declared preference, which
    /// is workflow_transition.sort_order. That is a real limitation of the
    /// state-oriented form and the reason the Workflow tab posts ACTIONS
    /// instead: only the caller knows whether they are rejecting something or
    /// asking for more work, and the difference is the entire value of the
    /// approval trail.
    /// </summary>
    public async Task<Result<WorkflowStepDefinition>> PerformToStateAsync(
        Product product,
        WorkflowState target,
        string? comment,
        string? actorDisplay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        var definition = await catalog.GetActiveAsync(ProductEntityType, cancellationToken);
        var current = definition.StepFor(product.WorkflowState);

        if (current is null)
        {
            return Result.Failure<WorkflowStepDefinition>(
                WorkflowErrors.StepNotInPipeline(product.WorkflowState.ToString()));
        }

        var candidates = definition.TransitionsFrom(current.Id)
            .Where(t => definition.StepById(t.ToStepId)?.State == target)
            .ToList();

        if (candidates.Count == 0)
        {
            return Result.Failure<WorkflowStepDefinition>(
                ProductErrors.InvalidWorkflowTransition(product.WorkflowState, target));
        }

        // Ordered by sort_order in the definition, so "first" is the pipeline's
        // own declared preference — REJECT before REQUEST_CHANGES on Approval,
        // both of which land on Draft.
        return await PerformAsync(product, candidates[0].Action, comment, actorDisplay, cancellationToken);
    }

    [LoggerMessage(EventId = 7300, Level = LogLevel.Information,
        Message = "Opened an approval task for {ProductCode} at step {Step}; it had none.")]
    private static partial void LogTaskOpened(ILogger logger, string productCode, string step);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning,
        Message = "Approval task for {ProductCode} was on step id {TaskStepId} but the product reads "
                + "{ProductStep} (step id {ProductStepId}). Realigned the task to the product.")]
    private static partial void LogProjectionDrift(
        ILogger logger, string productCode, long taskStepId, string productStep, long productStepId);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information,
        Message = "{ProductCode} moved {FromStep} -> {ToStep} by {Action}.")]
    private static partial void LogTransition(
        ILogger logger, string productCode, string fromStep, string toStep, WorkflowAction action);
}
