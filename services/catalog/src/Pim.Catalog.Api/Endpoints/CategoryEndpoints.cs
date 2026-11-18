using MediatR;
using Pim.Catalog.Application.Categories;

namespace Pim.Api.Endpoints;

/// <summary>
/// The Categories screen: the merchandising tree, and what one node demands of
/// the products beneath it.
///
/// Two endpoints rather than one. The tree is the same for everyone looking at
/// the screen and changes only when the taxonomy does; the detail panel changes
/// on every click. Returning them together would refetch several hundred nodes
/// to answer "what is in this node", which is the interaction the screen is
/// built around.
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Categories");

        group.MapGet("/categories/tree", async (
            string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCategoryTreeQuery(locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<IReadOnlyList<CategoryTreeNodeDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "The active merchandising taxonomy as a nested tree, with direct and " +
            "subtree product counts per node. Labels render in the requested locale " +
            "with a base-locale fallback, flagged per node; the shape of the tree " +
            "does not depend on the locale.");

        group.MapGet("/categories/{code}", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCategoryDetailQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<CategoryDetailDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary(
            "One category: its place in the tree, its label in every configured " +
            "locale, and its attribute template with real coverage over the " +
            "products in its subtree.");
    }
}
