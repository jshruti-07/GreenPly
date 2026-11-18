using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Workflow;

/// <summary>
/// Errors the workflow engine can actually return to a caller.
///
/// A MALFORMED PIPELINE is not in this list. A definition with two starting
/// steps or a transition pointing at a step of another definition is a broken
/// deployment, not a request the client got wrong, and every status code would
/// mislead: 400 tells them to fix a payload that was fine, 409 tells them to
/// reload, 500 is the truth but ErrorType.Failure is banned across this
/// solution for good reasons. Those cases throw
/// <see cref="InvalidWorkflowDefinitionException"/> instead, once, loudly, at
/// the point the pipeline is loaded.
/// </summary>
public static class WorkflowErrors
{
    public static Error UnknownAction(string action) =>
        Error.Validation("Workflow.UnknownAction",
            $"'{(string.IsNullOrWhiteSpace(action) ? "(none)" : action)}' is not a workflow action. "
            + $"Accepted: {string.Join(", ", WorkflowActions.All.Select(WorkflowActions.ToToken))}.");

    /// <summary>
    /// The action exists but not from where this product is standing — asking to
    /// approve something still in Draft. Conflict rather than Validation: the
    /// request was well formed and the world disagreed.
    /// </summary>
    public static Error ActionNotAvailable(string action, string step) =>
        Error.Conflict("Workflow.ActionNotAvailable",
            $"'{(string.IsNullOrWhiteSpace(action) ? "(none)" : action)}' is not available from the "
            + $"{(string.IsNullOrWhiteSpace(step) ? "current" : step)} step. Reload to see the current options.");

    public static readonly Error TaskAlreadyClosed =
        Error.Conflict("Workflow.TaskAlreadyClosed",
            "This approval task has already been completed and cannot be moved again.");

    /// <summary>
    /// The product is in a state the active pipeline has no step for — which
    /// happens if a definition is replaced by one that drops a step while
    /// products are standing in it.
    /// </summary>
    public static Error StepNotInPipeline(string state) =>
        Error.Conflict("Workflow.StepNotInPipeline",
            $"The active pipeline has no step for the state "
            + $"'{(string.IsNullOrWhiteSpace(state) ? "(unknown)" : state)}', so this product cannot be moved.");
}

/// <summary>
/// Thrown when workflow_definition, _step and _transition together do not
/// describe a usable pipeline. Not a domain result: nothing the caller sends
/// can cause it and nothing the caller does can fix it.
/// </summary>
public sealed class InvalidWorkflowDefinitionException : InvalidOperationException
{
    public InvalidWorkflowDefinitionException(string message)
        : base(message)
    {
    }

    public InvalidWorkflowDefinitionException()
        : base("The configured workflow pipeline is not usable.")
    {
    }

    public InvalidWorkflowDefinitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
