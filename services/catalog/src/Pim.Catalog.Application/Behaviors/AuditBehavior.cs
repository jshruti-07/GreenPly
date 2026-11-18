using MediatR;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Application.Auditing;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Behaviors;

/// <summary>
/// Records an audit entry for every successful <see cref="IAuditableCommand"/>.
///
/// Auditing lives in the pipeline rather than in each handler for one reason:
/// a handler that forgets to write its entry produces a history with a silent
/// hole, and nothing fails. Here, a command either carries the marker and is
/// audited, or it does not and the coverage test fails.
///
/// FAILED commands are deliberately NOT recorded. This is an activity log for
/// stewards — "what happened to this product" — not a security audit of attempts.
/// A rejected 409 changed nothing, and listing it would train readers to ignore
/// entries. If attempt-level logging is needed for security it belongs in a
/// separate stream with different retention.
///
/// The write is best-effort: it runs AFTER the command has committed, so a
/// failure here must not roll back or fail work the user already completed. A
/// dropped entry is logged as an error for operations to alert on.
/// </summary>
public sealed partial class AuditBehavior<TRequest, TResponse>(
    IAuditWriter writer,
    ILogger<AuditBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Distinguishes UI-driven changes from future imports or jobs.</summary>
    private const string Source = "api";

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var response = await next(cancellationToken);

        if (request is not IAuditableCommand auditable || !IsSuccess(response))
        {
            return response;
        }

        try
        {
            await writer.WriteAsync(
                auditable.ProductCode,
                auditable.AuditAction,
                auditable.AuditDetail,
                Source,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The user's change succeeded; failing their request now would be
            // worse than a gap in the timeline. Surface it loudly instead.
            AuditWriteFailed(logger, auditable.ProductCode, auditable.AuditAction, exception);
        }

        return response;
    }

    /// <summary>
    /// Every command in this assembly returns Result or Result&lt;T&gt;; both
    /// expose IsSuccess. Anything else is treated as successful because there
    /// is nothing to inspect.
    /// </summary>
    private static bool IsSuccess(TResponse response) =>
        response is not Result result || result.IsSuccess;

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Audit entry could not be written for product {ProductCode} action {Action}. The change itself succeeded.")]
    private static partial void AuditWriteFailed(
        ILogger logger, string productCode, string action, Exception exception);
}
