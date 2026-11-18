using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Domain.Workflow;

/// <summary>One step of a pipeline (workflow_step).</summary>
/// <param name="State">
/// The step's code as the product's workflow_state enum. Resolved once when the
/// definition is built, so an unknown code fails at load with a named error
/// rather than throwing on whichever request happens to touch that step first.
/// </param>
public sealed record WorkflowStepDefinition(
    long Id,
    string Code,
    string Name,
    int SortOrder,
    string? OwnerRole,
    bool IsInitial,
    bool IsTerminal,
    WorkflowState State);

/// <summary>One permitted move (workflow_transition).</summary>
public sealed record WorkflowTransitionDefinition(
    long Id,
    long FromStepId,
    long ToStepId,
    WorkflowAction Action,
    string Label,
    int SortOrder);

/// <summary>
/// A pipeline, loaded from workflow_definition + _step + _transition and held
/// immutable for the process lifetime.
///
/// Everything is validated at construction: one initial step, step codes that
/// map to real workflow states, transitions that point at steps of this
/// definition, and no two transitions leaving the same step under the same
/// action. A pipeline that fails any of those is not "mostly working" — it is a
/// deployment whose editorial process is undefined, and it should say so once
/// at startup rather than fail on the third approval of the day.
/// </summary>
public sealed class WorkflowDefinition : IWorkflowPolicy
{
    private readonly Dictionary<long, WorkflowStepDefinition> _byId;
    private readonly Dictionary<WorkflowState, WorkflowStepDefinition> _byState;
    private readonly Dictionary<long, List<WorkflowTransitionDefinition>> _outgoing;

    private WorkflowDefinition(
        long id,
        string code,
        string name,
        string entityType,
        int version,
        IReadOnlyList<WorkflowStepDefinition> steps,
        IReadOnlyList<WorkflowTransitionDefinition> transitions)
    {
        Id = id;
        Code = code;
        Name = name;
        EntityType = entityType;
        Version = version;
        Steps = steps;
        Transitions = transitions;

        _byId = steps.ToDictionary(s => s.Id);
        _byState = steps.ToDictionary(s => s.State);
        _outgoing = transitions
            .GroupBy(t => t.FromStepId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList());

        InitialStep = steps.Single(s => s.IsInitial);
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; }

    public string EntityType { get; }

    public int Version { get; }

    /// <summary>Steps in pipeline order.</summary>
    public IReadOnlyList<WorkflowStepDefinition> Steps { get; }

    public IReadOnlyList<WorkflowTransitionDefinition> Transitions { get; }

    public WorkflowStepDefinition InitialStep { get; }

    /// <summary>
    /// Builds and validates a pipeline. Throws
    /// <see cref="InvalidWorkflowDefinitionException"/> rather than returning a
    /// failed Result, because every one of these conditions is a broken
    /// deployment rather than a bad request — see WorkflowErrors for why that
    /// distinction is worth the exception.
    /// </summary>
    public static WorkflowDefinition Create(
        long id,
        string code,
        string name,
        string entityType,
        int version,
        IReadOnlyList<WorkflowStepDefinition> steps,
        IReadOnlyList<WorkflowTransitionDefinition> transitions)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(transitions);

        if (steps.Count == 0)
        {
            throw Invalid(code, "it has no steps");
        }

        var initialCount = steps.Count(s => s.IsInitial);
        if (initialCount != 1)
        {
            throw Invalid(code, $"it has {initialCount} starting steps and must have exactly one");
        }

        if (steps.Select(s => s.Id).Distinct().Count() != steps.Count)
        {
            throw Invalid(code, "two steps share an identifier");
        }

        if (steps.Select(s => s.State).Distinct().Count() != steps.Count)
        {
            // Two steps mapping to one workflow state would make the projection
            // on product.workflow_state ambiguous in the reverse direction.
            throw Invalid(code, "two steps map to the same product state");
        }

        var stepIds = steps.Select(s => s.Id).ToHashSet();
        var dangling = transitions.FirstOrDefault(
            t => !stepIds.Contains(t.FromStepId) || !stepIds.Contains(t.ToStepId));

        if (dangling is not null)
        {
            throw Invalid(code, $"transition {dangling.Id} points at a step outside this pipeline");
        }

        var ambiguous = transitions
            .GroupBy(t => (t.FromStepId, t.Action))
            .FirstOrDefault(g => g.Count() > 1);

        if (ambiguous is not null)
        {
            // The database enforces this too (UNIQUE on definition, from_step,
            // action). Checked here as well so a definition built in a test or
            // by a future admin screen cannot skip it.
            throw Invalid(code,
                $"action {WorkflowActions.ToToken(ambiguous.Key.Action)} leaves one step in {ambiguous.Count()} directions");
        }

        var ordered = steps.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToArray();

        return new WorkflowDefinition(id, code, name, entityType, version, ordered, transitions);
    }

    private static InvalidWorkflowDefinitionException Invalid(string code, string because) =>
        new($"Workflow definition '{code}' cannot be used because {because}. "
            + "Check workflow_definition, workflow_step and workflow_transition.");

    public WorkflowStepDefinition? StepById(long stepId) =>
        _byId.TryGetValue(stepId, out var step) ? step : null;

    public WorkflowStepDefinition? StepFor(WorkflowState state) =>
        _byState.TryGetValue(state, out var step) ? step : null;

    /// <summary>Moves available out of a step, in display order. Empty for a terminal step.</summary>
    public IReadOnlyList<WorkflowTransitionDefinition> TransitionsFrom(long stepId) =>
        _outgoing.TryGetValue(stepId, out var list) ? list : [];

    public WorkflowTransitionDefinition? Resolve(long stepId, WorkflowAction action)
    {
        foreach (var transition in TransitionsFrom(stepId))
        {
            if (transition.Action == action)
            {
                return transition;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool Allows(WorkflowState current, WorkflowState target)
    {
        var origin = StepFor(current);

        if (origin is null)
        {
            // A product sitting in a state this pipeline does not define cannot
            // be moved by this pipeline. Refusing is the safe answer; the
            // alternative is letting it move anywhere.
            return false;
        }

        foreach (var transition in TransitionsFrom(origin.Id))
        {
            if (StepById(transition.ToStepId) is { } destination && destination.State == target)
            {
                return true;
            }
        }

        return false;
    }
}
