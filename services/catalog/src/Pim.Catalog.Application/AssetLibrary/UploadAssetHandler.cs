using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Assets;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AssetLibrary;

/// <summary>
/// Ingest: hash, de-duplicate, store, derive, record.
///
/// The order matters and is not arbitrary.
///
/// 1. The stream is spooled to a temporary file while being hashed. Nothing is
///    buffered whole — the largest accepted upload is 1 GB — and the size is
///    MEASURED rather than taken from Content-Length, which is a claim.
/// 2. The checksum is looked up before anything is written. Re-uploading a file
///    the catalogue already holds returns the existing asset instead of a
///    second copy of the same bytes under a different id, which is how media
///    libraries silently triple in size.
/// 3. Only then is anything written to storage, and only then to the database.
///    A failure before step 3 leaves nothing behind but a temp file the OS
///    reclaims.
/// </summary>
public sealed partial class UploadAssetCommandHandler(
    IAssetStorage storage,
    IRenditionGenerator renditions,
    IAssetIngestStore store,
    DisplayLocaleResolver localeResolver,
    ILogger<UploadAssetCommandHandler> logger)
    : IRequestHandler<UploadAssetCommand, Result<IngestedAsset>>
{
    public async Task<Result<IngestedAsset>> Handle(
        UploadAssetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Content is null)
        {
            return Result.Failure<IngestedAsset>(AssetUploadErrors.FileRequired);
        }

        var fileName = request.FileName?.Trim() ?? string.Empty;

        // Declared type wins when given; otherwise inferred from the name. The
        // extension check below then has to agree with whichever it is, so a
        // caller cannot smuggle a .exe past by declaring IMAGE.
        var assetType = string.IsNullOrWhiteSpace(request.AssetType)
            ? AssetRules.InferType(fileName)
            : request.AssetType.Trim().ToUpperInvariant();

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<IngestedAsset>(locale.Error);
        }

        // Spool to disk. Deleted in the finally block whatever happens.
        var tempPath = Path.Combine(Path.GetTempPath(), $"pim-upload-{Guid.NewGuid():N}.tmp");

        try
        {
            string checksum;
            long byteCount;

            await using (var temp = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                long total = 0;
                var cap = AssetUploadRules.MaxBytesFor(assetType);
                int read;

                while ((read = await request.Content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;

                    // Enforced DURING the read, not after. Reading a hostile
                    // 40 GB body to completion in order to reject it is itself
                    // the denial of service.
                    if (total > cap)
                    {
                        return Result.Failure<IngestedAsset>(
                            AssetUploadErrors.TooLarge(assetType, total, cap));
                    }

                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                sha.TransformFinalBlock([], 0, 0);
                checksum = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                byteCount = total;
            }

            var validation = AssetUploadRules.Validate(assetType, fileName, byteCount);
            if (validation.IsFailure)
            {
                return Result.Failure<IngestedAsset>(validation.Error);
            }

            var existing = await store.FindByChecksumAsync(checksum, cancellationToken);
            if (existing is Guid duplicate)
            {
                // Not an error. The caller asked for this file to be in the
                // library and it is; saying so beats both a second copy and a
                // 409 the UI would have to translate back into "fine".
                LogDeduplicated(logger, fileName, duplicate);

                return Result.Success(new IngestedAsset(duplicate, Deduplicated: true));
            }

            var extension = AssetUploadRules.ExtensionOf(fileName);
            var prefix = $"{checksum[..2]}/{checksum}";
            var originalKey = $"{prefix}/original{extension}";

            await using (var source = OpenTemp(tempPath))
            {
                await storage.SaveAsync(originalKey, source, cancellationToken);
            }

            int? width = null;
            int? height = null;
            var stored = new List<StoredRendition>();

            await using (var source = OpenTemp(tempPath))
            {
                var probe = await renditions.ProbeAsync(source, cancellationToken);
                width = probe?.Width;
                height = probe?.Height;
            }

            // The original is recorded as a rendition in its own right. Sprint
            // 25 established why: a URL that does not say which derivative it
            // is will eventually be served as a thumbnail.
            stored.Add(new StoredRendition(
                "original", originalKey, width, height,
                extension.Length > 1 ? extension[1..] : null));

            await using (var source = OpenTemp(tempPath))
            {
                var generated = await renditions.GenerateAsync(source, assetType, cancellationToken);

                foreach (var rendition in generated)
                {
                    var key = $"{prefix}/{rendition.Role}.{rendition.Format}";
                    using var content = new MemoryStream(rendition.Content, writable: false);
                    await storage.SaveAsync(key, content, cancellationToken);

                    stored.Add(new StoredRendition(
                        rendition.Role, key, rendition.Width, rendition.Height, rendition.Format));
                }
            }

            var publicId = await store.InsertAsync(
                new AssetIngestRequest(
                    storage.ProviderName,
                    originalKey,
                    fileName,
                    string.IsNullOrWhiteSpace(request.ContentType)
                        ? AssetRules.InferMimeType(fileName)
                        : request.ContentType,
                    assetType,
                    byteCount,
                    checksum,
                    width,
                    height,
                    string.IsNullOrWhiteSpace(request.UsageRights) ? null : request.UsageRights.Trim(),
                    request.RightsExpiry,
                    string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                    string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText.Trim(),
                    locale.Value.Code,
                    stored),
                cancellationToken);

            LogStored(logger, publicId, assetType, byteCount, stored.Count);

            return Result.Success(new IngestedAsset(publicId, Deduplicated: false));
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    // Source-generated logging, as the analysers require across this solution
    // (CA1848/CA1873): the delegate is built once and the arguments are not
    // evaluated when the level is disabled.
    [LoggerMessage(
        EventId = 7210,
        Level = LogLevel.Information,
        Message = "Upload of {FileName} matched existing asset {AssetId} by checksum; not stored again.")]
    private static partial void LogDeduplicated(ILogger logger, string fileName, Guid assetId);

    [LoggerMessage(
        EventId = 7211,
        Level = LogLevel.Information,
        Message = "Stored asset {AssetId} ({AssetType}, {Bytes} bytes) with {RenditionCount} renditions.")]
    private static partial void LogStored(
        ILogger logger, Guid assetId, string assetType, long bytes, int renditionCount);

    private static FileStream OpenTemp(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A temp file that will not delete is not worth failing an upload
            // that otherwise succeeded. The OS reclaims it.
        }
    }
}
