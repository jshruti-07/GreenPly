using MediatR;
using Pim.Catalog.Application.AssetLibrary;
using Pim.Catalog.Domain.Assets;

namespace Pim.Api.Endpoints;

/// <summary>
/// Asset ingest and delivery.
///
/// SECURITY, stated here because it is the first write endpoint that accepts
/// arbitrary bytes: there is no authentication on this API (R-3), so anything
/// that can reach it can put a file in the store. That is accepted for LOCAL
/// DEVELOPMENT ONLY (decision 2026-08-19) and is recorded as a release blocker
/// in R-24. This endpoint must not be reachable from outside the dev machine
/// until authentication exists.
/// </summary>
public static class AssetIngestEndpoints
{
    public static void MapAssetIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Digital Assets");

        group.MapPost("/assets", async (
            IFormFile file,
            ISender sender,
            CancellationToken ct,
            string? assetType = null,
            string? title = null,
            string? altText = null,
            string? usageRights = null,
            DateOnly? rightsExpiry = null,
            string? locale = null) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    title: AssetUploadErrors.Empty.Code,
                    detail: AssetUploadErrors.Empty.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await using var content = file.OpenReadStream();

            var result = await sender.Send(
                new UploadAssetCommand(
                    content,
                    file.FileName,
                    file.ContentType,
                    assetType,
                    title,
                    altText,
                    usageRights,
                    rightsExpiry,
                    locale),
                ct);

            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            // 200 for a file the library already had, 201 for one it did not.
            // The status carries the information rather than the caller having
            // to read a flag to find out whether anything was created — though
            // the flag is there too, because a UI wants to say "already in the
            // library" rather than "uploaded".
            var value = result.Value;
            return value.Deduplicated
                ? Results.Ok(value)
                : Results.Created($"/api/v1/assets/{value.PublicId}", value);
        })
        // Multipart form binding in minimal APIs is antiforgery-protected by
        // default. There is no antiforgery middleware in this pipeline and no
        // cookie auth for it to protect, so the check would reject every upload
        // for a threat model that does not apply here. Revisit when auth lands.
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<IngestedAsset>(StatusCodes.Status201Created)
        .Produces<IngestedAsset>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Upload a file into the asset library. De-duplicated by SHA-256: an " +
            "identical file returns the existing asset with 200 rather than storing " +
            "a second copy. Images get thumb (320px) and web (1600px) derivatives; " +
            "BIM, CAD and documents are stored as-is. NO AUTHENTICATION — local " +
            "development only, see R-24.");

        group.MapGet("/assets/{publicId:guid}/file", async (
            Guid publicId,
            IAssetIngestStore store,
            IAssetStorage storage,
            CancellationToken ct,
            string rendition = "original") =>
        {
            var location = await store.FindBinaryAsync(publicId, rendition, ct);

            if (location is null)
            {
                return Results.Problem(
                    title: "Asset.NotFound",
                    detail: $"Asset '{publicId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // Assets imported from the client's media system are absolute URLs
            // this service does not hold. Redirecting is honest and useful:
            // the caller gets the bytes, and the Location header says plainly
            // that they came from somewhere else. Proxying them would make this
            // service responsible for someone else's uptime.
            if (!string.Equals(location.StorageProvider, storage.ProviderName, StringComparison.Ordinal))
            {
                return Uri.TryCreate(location.Uri, UriKind.Absolute, out var external)
                    ? Results.Redirect(external.ToString(), permanent: false)
                    : Results.Problem(
                        title: AssetUploadErrors.NotStoredHere(location.StorageProvider).Code,
                        detail: AssetUploadErrors.NotStoredHere(location.StorageProvider).Message,
                        statusCode: StatusCodes.Status409Conflict);
            }

            var stream = await storage.OpenReadAsync(location.Uri, ct);

            if (stream is null)
            {
                // The row says the file is here and it is not. Worth a distinct
                // answer from "no such asset": one is a bad id, the other is a
                // store that has lost a file it recorded.
                return Results.Problem(
                    title: "Asset.BinaryMissing",
                    detail: $"Asset '{publicId}' is recorded as stored here, but its binary is missing from the store.",
                    statusCode: StatusCodes.Status410Gone);
            }

            return Results.File(
                stream,
                location.MimeType ?? "application/octet-stream",
                location.FileName,
                enableRangeProcessing: true);
        })
        .Produces<IResult>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status410Gone)
        .WithSummary(
            "Stream an asset's bytes. ?rendition=thumb|web|original, falling back " +
            "to the original when the requested derivative does not exist — there " +
            "is no thumbnail of a Revit family, and a 404 there would be wrong. " +
            "Assets held in the client's media system are redirected to, not proxied.");
    }
}
