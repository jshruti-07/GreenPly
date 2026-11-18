using Microsoft.AspNetCore.Mvc;

namespace Pim.Api.Middleware;

/// <summary>
/// Last-resort handler: unexpected exceptions become RFC 7807 responses with a
/// correlation id; details stay in the logs, never in the response body.
/// </summary>
public sealed partial class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.TraceIdentifier;
            Log.UnhandledException(logger, ex, correlationId);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }

    /// <summary>Source-generated logging (CA1848/CA1873-compliant).</summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "Unhandled exception. CorrelationId={CorrelationId}")]
        public static partial void UnhandledException(ILogger logger, Exception exception, string correlationId);
    }
}
