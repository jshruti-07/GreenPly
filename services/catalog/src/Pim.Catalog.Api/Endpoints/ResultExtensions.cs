using Microsoft.AspNetCore.Mvc;
using Pim.SharedKernel.Results;

namespace Pim.Api.Endpoints;

public static class ResultExtensions
{
    /// <summary>Maps a failed domain result to an RFC 7807 response.</summary>
    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot map a successful result to a problem response.");
        }

        var statusCode = result.Error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = result.Error.Code,
            Detail = result.Error.Message
        });
    }
}
