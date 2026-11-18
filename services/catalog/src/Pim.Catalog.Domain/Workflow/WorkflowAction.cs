namespace Pim.Catalog.Domain.Workflow;

/// <summary>
/// What a person does to a product in the editorial pipeline, per Database
/// Design v2.0 §12 (workflow_transition.action).
///
/// The action is the verb; where it lands is configuration. REJECT and
/// REQUEST_CHANGES both send a product back to Draft in the shipped pipeline
/// and are still two actions, because "this is wrong" and "this needs more
/// work" are different things to read in an approval trail six months later.
/// </summary>
public enum WorkflowAction
{
    Submit,
    Approve,
    Reject,
    RequestChanges,
    Publish
}

/// <summary>
/// Enum to database token and back.
///
/// This exists as an explicit table rather than as ToUpperInvariant() because
/// RequestChanges is REQUEST_CHANGES and casing alone does not get you there.
/// A silent mismatch would make one transition permanently unreachable while
/// every other action kept working — the kind of defect that looks like a
/// missing button. WorkflowActionTests round-trips every member.
/// </summary>
public static class WorkflowActions
{
    private static readonly (WorkflowAction Action, string Token)[] Table =
    [
        (WorkflowAction.Submit, "SUBMIT"),
        (WorkflowAction.Approve, "APPROVE"),
        (WorkflowAction.Reject, "REJECT"),
        (WorkflowAction.RequestChanges, "REQUEST_CHANGES"),
        (WorkflowAction.Publish, "PUBLISH")
    ];

    /// <summary>Every action, in declaration order. Used by tests and by the API contract.</summary>
    public static IReadOnlyList<WorkflowAction> All { get; } =
        Table.Select(entry => entry.Action).ToArray();

    public static string ToToken(WorkflowAction action)
    {
        foreach (var entry in Table)
        {
            if (entry.Action == action)
            {
                return entry.Token;
            }
        }

        // Unreachable while the table covers the enum, which WorkflowActionTests
        // asserts. Throwing beats returning a token no transition will match.
        throw new ArgumentOutOfRangeException(nameof(action), action, "No database token for this workflow action.");
    }

    /// <summary>
    /// Accepts the database token (REQUEST_CHANGES) and the enum name
    /// (RequestChanges), because the first arrives from SQL and the second from
    /// a JSON body typed by a front-end developer.
    /// </summary>
    public static bool TryParse(string? value, out WorkflowAction action)
    {
        action = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        foreach (var entry in Table)
        {
            if (string.Equals(trimmed, entry.Token, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, entry.Action.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                action = entry.Action;
                return true;
            }
        }

        return false;
    }
}
