using Pim.Catalog.Application.Common;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// One product as the board draws it, per prototype v4.2 renderWorkflows():
/// code, category, completeness and who has it.
/// </summary>
/// <param name="Assignee">
/// From the open approval task. NULL for a product that has never been through
/// the pipeline — which is most of the catalogue — and deliberately not
/// back-filled from <paramref name="Owner"/>: owning a product and being asked
/// to act on it are different claims.
/// </param>
public sealed record WorkflowCardDto(
    Guid PublicId,
    string Code,
    string Name,
    string Category,
    int CompletenessPct,
    string Step,
    string? Owner,
    string? Assignee,
    string? Priority,
    DateOnly? DueDate,
    DateTimeOffset UpdatedAt,
    bool HasOpenTask);

/// <summary>
/// One column. <paramref name="TotalCount"/> is the whole column, not the
/// number of cards returned — the two differ for every real catalogue, and a
/// count that silently means "the first twelve" is the kind of figure R-19 is
/// about.
/// </summary>
/// <param name="IsRecognised">
/// False for a workflow_state the active pipeline does not define. Such a
/// column is SHOWN rather than dropped: discarding the rows would make every
/// other column look like a larger share of a catalogue that had quietly
/// shrunk. Sprint 21 shipped exactly that bug on the cockpit's pipeline panel.
/// </param>
public sealed record WorkflowColumnDto(
    string Step,
    string Name,
    int SortOrder,
    bool IsTerminal,
    string? OwnerRole,
    int TotalCount,
    bool IsRecognised,
    IReadOnlyList<WorkflowCardDto> Cards);

public sealed record WorkflowBoardDto(
    string DefinitionCode,
    string DefinitionName,
    int TotalProducts,
    int CardsPerColumn,
    IReadOnlyList<WorkflowColumnDto> Columns);

/// <summary>One row of the list view.</summary>
public sealed record WorkflowQueueItemDto(
    Guid PublicId,
    string Code,
    string Name,
    string Category,
    int CompletenessPct,
    string Step,
    string StepName,
    string? Owner,
    string? Assignee,
    string? Priority,
    DateOnly? DueDate,
    DateTimeOffset UpdatedAt,
    string? LastAction,
    DateTimeOffset? LastActionAt);

/// <summary>
/// The list view, with its own filter options in the same response.
///
/// One request rather than two, for the reason the cockpit gives: a filter list
/// fetched separately can describe a catalogue the rows below it no longer
/// match, and nothing on screen says which half is stale.
/// </summary>
public sealed record WorkflowQueueDto(
    PagedResult<WorkflowQueueItemDto> Page,
    IReadOnlyList<string> Assignees,
    IReadOnlyList<WorkflowStepDto> Steps);
