using MediatR;
using Pim.Catalog.Application.Translations;

namespace Pim.Api.Endpoints;

/// <summary>
/// The Translation Workbench: how far each language has got, and which products
/// are holding it up.
///
/// Two endpoints, split the way the screen is read. The progress summary is
/// small, changes only when content does, and is what the page opens with; the
/// queue is paged, filtered and re-fetched on every interaction. Serving them
/// together would re-count the whole catalogue per language every time somebody
/// paged a table.
/// </summary>
public static class TranslationEndpoints
{
    public static void MapTranslationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Translations");

        group.MapGet("/translations", async (
            string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetTranslationWorkbenchQuery(locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<TranslationWorkbenchDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Per-language translation progress: products with text, missing, " +
            "outdated (base edited since), awaiting review and approved. Panels " +
            "v4.2 draws that need a machine-translation service are returned in " +
            "omittedPanels with the reason rather than left blank.");

        group.MapGet("/translations/queue", async (
            string target,
            string? state,
            string? search,
            string? locale,
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25) =>
        {
            var result = await sender.Send(
                new GetTranslationQueueQuery(target, state, search, page, pageSize, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<TranslationQueueDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Products needing work in one target language. state=attention (the " +
            "default: missing, outdated or not yet approved), missing, outdated, " +
            "review or all. `target` is the language being translated INTO; " +
            "`locale` is the language the rows are rendered in, which is a " +
            "different question and usually a different answer.");
    }
}
