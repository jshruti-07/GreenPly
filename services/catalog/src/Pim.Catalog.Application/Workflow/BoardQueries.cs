using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// The Workflows &amp; Tasks board — prototype v4.2 renderWorkflows().
/// </summary>
/// <param name="CardsPerColumn">
/// How many cards each column carries. The board cannot show a 500-product
/// catalogue, so it shows the most recently touched of each column and reports
/// the real total alongside. A count that means "the first twelve" while
/// looking like "all of them" is the defect class R-19 names.
/// </param>
public sealed record GetWorkflowBoardQuery(int CardsPerColumn = 12, string? Locale = null)
    : IRequest<Result<WorkflowBoardDto>>;

public sealed class GetWorkflowBoardQueryValidator : AbstractValidator<GetWorkflowBoardQuery>
{
    public GetWorkflowBoardQueryValidator()
    {
        RuleFor(q => q.CardsPerColumn)
            .InclusiveBetween(1, 50)
            .WithMessage("cardsPerColumn must be between 1 and 50. The board is a summary, not a list — use the list view for the whole column.");
    }
}

public sealed class GetWorkflowBoardQueryHandler(
    IWorkflowBoardStore store,
    IWorkflowCatalog catalog,
    DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetWorkflowBoardQuery, Result<WorkflowBoardDto>>
{
    public async Task<Result<WorkflowBoardDto>> Handle(
        GetWorkflowBoardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<WorkflowBoardDto>(locale.Error);
        }

        var definition = await catalog.GetActiveAsync(
            WorkflowTransitionService.ProductEntityType, cancellationToken);

        var totals = await store.GetStateTotalsAsync(cancellationToken);
        var cards = await store.GetCardsAsync(request.CardsPerColumn, locale.Value, cancellationToken);

        var totalByState = totals
            .GroupBy(t => t.WorkflowState, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Total), StringComparer.OrdinalIgnoreCase);

        var cardsByState = cards
            .GroupBy(c => c.WorkflowState, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(ToCard).ToList(), StringComparer.OrdinalIgnoreCase);

        var columns = definition.Steps
            .Select(step => new WorkflowColumnDto(
                step.Code,
                step.Name,
                step.SortOrder,
                step.IsTerminal,
                step.OwnerRole,
                totalByState.TryGetValue(step.Code, out var total) ? total : 0,
                IsRecognised: true,
                cardsByState.TryGetValue(step.Code, out var list) ? list : []))
            .ToList();

        // States present in the catalogue that the pipeline does not define.
        // Shown, not dropped: Sprint 21's cockpit dropped two and the panel
        // summed to half the catalogue while looking entirely plausible.
        var known = definition.Steps.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        columns.AddRange(totals
            .GroupBy(t => t.WorkflowState, StringComparer.OrdinalIgnoreCase)
            .Where(g => !known.Contains(g.Key))
            .Select(g => new WorkflowColumnDto(
                g.Key,
                $"{g.Key} (not in this pipeline)",
                int.MaxValue,
                IsTerminal: false,
                OwnerRole: null,
                g.Sum(t => t.Total),
                IsRecognised: false,
                cardsByState.TryGetValue(g.Key, out var list) ? list : [])));

        return Result.Success(new WorkflowBoardDto(
            definition.Code,
            definition.Name,
            totals.Sum(t => t.Total),
            request.CardsPerColumn,
            columns));
    }

    private static WorkflowCardDto ToCard(WorkflowCardRow row) =>
        new(row.PublicId, row.Code, row.Name, row.Category, row.CompletenessPct,
            row.WorkflowState, row.OwnerDisplayName, row.AssigneeDisplayName,
            row.Priority, row.DueDate, row.UpdatedAt, row.HasOpenTask);
}

/// <summary>
/// The list view: the same pipeline as a filterable table, and "my queue" when
/// filtered by assignee.
/// </summary>
public sealed record GetWorkflowQueueQuery(
    int Page = 1,
    int PageSize = 25,
    string? Step = null,
    string? Assignee = null,
    string? Priority = null,
    string? Search = null,
    // Literals, not the constants below: a primary constructor's default values
    // are outside the scope of the members declared in its own body.
    string SortBy = "updated",
    string SortDir = "desc",
    string? Locale = null)
    : IRequest<Result<WorkflowQueueDto>>
{
    public const string DefaultSortBy = "updated";
    public const string DefaultSortDir = "desc";

    /// <summary>
    /// The single list both the validator and the SQL store honour. Priority is
    /// absent on purpose: its values sort alphabetically as high, low, normal,
    /// urgent, which is not the order anybody means.
    /// </summary>
    public static IReadOnlyList<string> SortableColumns { get; } =
        ["code", "name", "category", "step", "owner", "assignee", "due", "updated"];
}

public sealed class GetWorkflowQueueQueryValidator : AbstractValidator<GetWorkflowQueueQuery>
{
    public GetWorkflowQueueQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.SortBy)
            .Must(s => GetWorkflowQueueQuery.SortableColumns.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(q =>
                $"sortBy '{q.SortBy}' is not sortable. Allowed: {string.Join(", ", GetWorkflowQueueQuery.SortableColumns)}.");

        RuleFor(q => q.SortDir)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be 'asc' or 'desc'.");

        // Rejected rather than answered with an empty list. An empty queue reads
        // as "nothing to do", which is the most misleading thing this screen
        // could say — the same reason Sprint 24 rejects the base locale as a
        // translation target.
        RuleFor(q => q.Step)
            .Must(s => s is null || Enum.TryParse<WorkflowState>(s, ignoreCase: true, out _))
            .WithMessage(q =>
                $"step '{q.Step}' is unknown. Allowed: {string.Join(", ", Enum.GetNames<WorkflowState>().Select(n => n.ToLowerInvariant()))}.");

        RuleFor(q => q.Priority)
            .Must(p => p is null || Enum.TryParse<ApprovalPriority>(p, ignoreCase: true, out _))
            .WithMessage(q =>
                $"priority '{q.Priority}' is unknown. Allowed: {string.Join(", ", Enum.GetNames<ApprovalPriority>().Select(n => n.ToLowerInvariant()))}.");

        RuleFor(q => q.Search).MaximumLength(200);
    }
}

public sealed class GetWorkflowQueueQueryHandler(
    IWorkflowBoardStore store,
    IWorkflowCatalog catalog,
    DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetWorkflowQueueQuery, Result<WorkflowQueueDto>>
{
    public async Task<Result<WorkflowQueueDto>> Handle(
        GetWorkflowQueueQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<WorkflowQueueDto>(locale.Error);
        }

        var definition = await catalog.GetActiveAsync(
            WorkflowTransitionService.ProductEntityType, cancellationToken);

        var stepNames = definition.Steps.ToDictionary(
            s => s.Code, s => s.Name, StringComparer.OrdinalIgnoreCase);

        var page = await store.GetQueueAsync(
            new WorkflowQueueFilter(
                request.Page,
                request.PageSize,
                request.Step,
                request.Assignee,
                request.Priority,
                request.Search,
                request.SortBy,
                request.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase),
                locale.Value),
            cancellationToken);

        var assignees = await store.GetAssigneesAsync(cancellationToken);

        var items = page.Items
            .Select(row => new WorkflowQueueItemDto(
                row.PublicId,
                row.Code,
                row.Name,
                row.Category,
                row.CompletenessPct,
                row.WorkflowState,
                // A state the pipeline does not define keeps its raw value
                // rather than being rendered blank: an empty cell reads as
                // missing data rather than as an unrecognised state.
                stepNames.TryGetValue(row.WorkflowState, out var name) ? name : row.WorkflowState,
                row.OwnerDisplayName,
                row.AssigneeDisplayName,
                row.Priority,
                row.DueDate,
                row.UpdatedAt,
                row.LastAction,
                row.LastActionAt))
            .ToList();

        return Result.Success(new WorkflowQueueDto(
            new PagedResult<WorkflowQueueItemDto>(items, page.Page, page.PageSize, page.TotalCount),
            assignees,
            definition.Steps
                .Select(s => new WorkflowStepDto(
                    s.Code, s.Name, s.SortOrder, s.OwnerRole, s.IsTerminal,
                    IsCurrent: false, IsPassed: false))
                .ToList()));
    }
}
