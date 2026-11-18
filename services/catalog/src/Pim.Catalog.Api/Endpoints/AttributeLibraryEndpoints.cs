using MediatR;
using Pim.Catalog.Application.AttributeLibrary;

namespace Pim.Api.Endpoints;

/// <summary>
/// The Attribute Library: every attribute the catalogue defines, what it is,
/// where it is used, and how far it is filled in.
///
/// Unpaged. The catalogue defines a few dozen attributes and the screen filters
/// them client-side, which keeps group counts and type filters instant. Said
/// out loud in the payload (totalAttributes) so that growing past a few hundred
/// becomes a decision somebody takes rather than a limit somebody discovers.
/// </summary>
public static class AttributeLibraryEndpoints
{
    public static void MapAttributeLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Attribute Library");

        group.MapGet("/attributes", async (
            string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAttributeLibraryQuery(locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<AttributeLibraryDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Every attribute the catalogue defines, with its group, type, unit, " +
            "option count, set membership and coverage. Coverage is measured " +
            "against the products whose attribute set includes the attribute, not " +
            "against the whole catalogue.");

        group.MapGet("/attributes/{code}", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAttributeDetailQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<AttributeDetailDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary(
            "One attribute: its definition, its text in every configured locale " +
            "including the ones with none, its options with real usage counts, and " +
            "the attribute sets that include it.");
    }
}
