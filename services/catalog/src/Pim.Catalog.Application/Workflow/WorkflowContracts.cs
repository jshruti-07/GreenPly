namespace Pim.Catalog.Application.Workflow;

/// <summary>One step as the UI draws it.</summary>
public sealed record WorkflowStepDto(
    string Code,
    string Name,
    int SortOrder,
    string? OwnerRole,
    bool IsTerminal,
    bool IsCurrent,
    bool IsPassed);

/// <summary>
/// A move the caller may make right now. The UI renders one button per entry
/// and posts <see cref="Action"/> back — it does not decide what is possible,
/// which is the whole reason the front-end copy of the transition table was
/// deleted in Sprint 27.
/// </summary>
public sealed record WorkflowActionDto(
    string Action,
    string Label,
    string TargetStep,
    string TargetStepName);

public sealed record WorkflowHistoryDto(
    string Action,
    string? FromStep,
    string ToStep,
    string? Actor,
    string? Comment,
    DateTimeOffset OccurredAt);

/// <summary>
/// Everything the Workflow tab needs in one response: where the product is,
/// where it can go, who has it, and what has happened to it.
/// </summary>
/// <param name="RowVersion">
/// Carried so the tab can post an action without a second round trip for the
/// concurrency token.
/// </param>
/// <param name="ActorAttribution">
/// A plain-language statement of what the History below is worth. It is
/// rendered, not hidden: with no authentication every actor is unverified, and
/// a trail that looks authoritative while being anonymous is worse than one
/// that says so (R-26).
/// </param>
public sealed record ProductWorkflowDto(
    string ProductCode,
    string DefinitionCode,
    string DefinitionName,
    int DefinitionVersion,
    string CurrentStep,
    string CurrentStepName,
    string? CurrentStepOwnerRole,
    bool IsComplete,
    string? Assignee,
    string Priority,
    DateOnly? DueDate,
    string TaskStatus,
    int RowVersion,
    string ActorAttribution,
    IReadOnlyList<WorkflowStepDto> Steps,
    IReadOnlyList<WorkflowActionDto> AvailableActions,
    IReadOnlyList<WorkflowHistoryDto> History);

/// <summary>The configured pipeline itself, for anything that needs the shape rather than a product's position.</summary>
public sealed record WorkflowTransitionDto(
    string FromStep,
    string ToStep,
    string Action,
    string Label,
    int SortOrder);

public sealed record WorkflowDefinitionDto(
    string Code,
    string Name,
    string EntityType,
    int Version,
    IReadOnlyList<WorkflowStepDto> Steps,
    IReadOnlyList<WorkflowTransitionDto> Transitions);
