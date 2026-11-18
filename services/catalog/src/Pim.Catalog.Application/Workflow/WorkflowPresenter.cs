using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// Turns "a product plus the active pipeline" into the shape the Workflow tab
/// draws.
///
/// It is a service and not a private method of the query handler because the
/// POST that performs an action returns the same document as the GET that
/// reads it. Two builders would drift, and the one that drifted would be the
/// one the user sees immediately after clicking Approve.
/// </summary>
public sealed class WorkflowPresenter(IWorkflowCatalog catalog, IWorkflowReadStore store)
{
    /// <summary>
    /// Rendered in the History panel rather than kept in a risk register the
    /// user will never open. A trail that looks authoritative while being
    /// anonymous is worse than one that admits it (R-26).
    /// </summary>
    private const string UnverifiedAttribution =
        "Names in this trail are unverified: there is no sign-in yet, so each entry records "
        + "what the caller supplied rather than who they are.";

    public async Task<Result<ProductWorkflowDto>> BuildAsync(
        string productCode, CancellationToken cancellationToken)
    {
        var row = await store.GetProductWorkflowAsync(productCode, cancellationToken);

        if (row is null)
        {
            return Result.Failure<ProductWorkflowDto>(ProductErrors.NotFound(productCode));
        }

        var definition = await catalog.GetActiveAsync(
            WorkflowTransitionService.ProductEntityType, cancellationToken);

        if (!Enum.TryParse<WorkflowState>(row.WorkflowState, ignoreCase: true, out var state)
            || definition.StepFor(state) is not { } current)
        {
            return Result.Failure<ProductWorkflowDto>(
                WorkflowErrors.StepNotInPipeline(row.WorkflowState));
        }

        var history = row.TaskId is long taskId
            ? await store.GetHistoryAsync(taskId, cancellationToken)
            : [];

        var steps = definition.Steps
            .Select(s => new WorkflowStepDto(
                s.Code,
                s.Name,
                s.SortOrder,
                s.OwnerRole,
                s.IsTerminal,
                IsCurrent: s.Id == current.Id,
                IsPassed: s.SortOrder < current.SortOrder))
            .ToList();

        var actions = definition.TransitionsFrom(current.Id)
            .Select(t => new
            {
                Transition = t,
                Target = definition.StepById(t.ToStepId)
            })
            // Create() rejects dangling transitions, so Target is never null.
            // Filtering rather than dereferencing keeps a future definition
            // built some other way from producing a null reference here.
            .Where(x => x.Target is not null)
            .Select(x => new WorkflowActionDto(
                WorkflowActions.ToToken(x.Transition.Action),
                x.Transition.Label,
                x.Target!.Code,
                x.Target.Name))
            .ToList();

        return Result.Success(new ProductWorkflowDto(
            row.ProductCode,
            definition.Code,
            definition.Name,
            definition.Version,
            current.Code,
            current.Name,
            current.OwnerRole,
            IsComplete: current.IsTerminal,
            // Falls back to the product's owner when no task has been opened
            // yet, which is who would be asked to move it. Marked in the DTO
            // only by being the same person — the UI does not claim it is an
            // assignment, because it is not one.
            Assignee: row.AssigneeDisplayName ?? row.OwnerDisplayName,
            Priority: row.Priority ?? "normal",
            row.DueDate,
            // "none" rather than "open": a product that has never been through
            // the pipeline has no task, and saying "open" would invent one.
            TaskStatus: row.TaskStatus ?? "none",
            row.RowVersion,
            UnverifiedAttribution,
            steps,
            actions,
            history
                .Select(h => new WorkflowHistoryDto(
                    h.Action, h.FromStepCode, h.ToStepCode, h.ActorDisplay, h.Comment, h.OccurredAt))
                .ToList()));
    }

    public async Task<WorkflowDefinitionDto> BuildDefinitionAsync(
        string entityType, CancellationToken cancellationToken)
    {
        var definition = await catalog.GetActiveAsync(entityType, cancellationToken);

        return new WorkflowDefinitionDto(
            definition.Code,
            definition.Name,
            definition.EntityType,
            definition.Version,
            definition.Steps
                .Select(s => new WorkflowStepDto(
                    s.Code, s.Name, s.SortOrder, s.OwnerRole, s.IsTerminal,
                    IsCurrent: false, IsPassed: false))
                .ToList(),
            definition.Transitions
                .Select(t => new
                {
                    From = definition.StepById(t.FromStepId),
                    To = definition.StepById(t.ToStepId),
                    Transition = t
                })
                .Where(x => x.From is not null && x.To is not null)
                .OrderBy(x => x.From!.SortOrder)
                .ThenBy(x => x.Transition.SortOrder)
                .Select(x => new WorkflowTransitionDto(
                    x.From!.Code, x.To!.Code,
                    WorkflowActions.ToToken(x.Transition.Action),
                    x.Transition.Label,
                    x.Transition.SortOrder))
                .ToList());
    }
}
