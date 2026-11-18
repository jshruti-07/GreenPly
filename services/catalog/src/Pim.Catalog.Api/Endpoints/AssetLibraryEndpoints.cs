using MediatR;
using Pim.Catalog.Application.AssetLibrary;

namespace Pim.Api.Endpoints;

/// <summary>
/// The Digital Assets library: every asset the catalogue knows about, what it
/// is, where it is used, and what is wrong with it.
///
/// Read-only in this sprint. Ingest — upload, checksum de-duplication and
/// rendition generation — arrives next, behind a storage provider that starts
/// on local disk. What this build cannot do is stated in the summary's
/// omittedPanels rather than left for the reader to discover.
/// </summary>
public static class AssetLibraryEndpoints
{
    public static void MapAssetLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Digital Assets");

        group.MapGet("/assets/summary", async (
            string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAssetLibrarySummaryQuery(locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<AssetLibrarySummaryDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Library totals by asset type, plus the two figures no per-product " +
            "screen can answer: assets linked to nothing, and products with no " +
            "main image. Also reports rights expiring within 90 days.");

        group.MapGet("/assets", async (
            string? type,
            string? usage,
            string? search,
            string? locale,
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 24) =>
        {
            var result = await sender.Send(
                new GetAssetsQuery(type, usage, search, page, pageSize, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<AssetPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Assets, paged. Filter by type (IMAGE, BIM, CAD, DOCUMENT, ...) and by " +
            "usage (all, orphan, linked); search matches the file name or a title in " +
            "any language. Every item names the rendition its URL points at — this " +
            "build generates none, so that is always the original.");

        group.MapGet("/assets/{publicId:guid}", async (
            Guid publicId, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAssetDetailQuery(publicId, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<AssetDetailDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary(
            "One asset: storage location, checksum, dimensions, rights, its " +
            "renditions, its title/alt/caption in every configured locale including " +
            "the ones with none, and every product that uses it.");
    }
}
