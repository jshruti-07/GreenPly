using Microsoft.Extensions.Logging;
using Pim.Catalog.Application.AssetLibrary;
using Pim.Catalog.Domain.Assets;
using SkiaSharp;

namespace Pim.Catalog.Infrastructure.Assets;

/// <summary>
/// Thumbnails and web-sized images, with SkiaSharp.
///
/// SkiaSharp because it is MIT licensed. The obvious alternative,
/// SixLabors.ImageSharp, moved to the Six Labors Split License at v3: free for
/// open source and for companies under a revenue threshold, commercial licence
/// above it. That is a cost decision, and quietly taking it on the client's
/// behalf inside a sprint would be the wrong way for them to find out.
///
/// Two derivatives, both JPEG:
///   thumb — 320px on the longest edge, for grids and lists
///   web   — 1600px on the longest edge, for a product page
///
/// Neither ever UPSCALES. A 200px source produces no thumb and no web rendition,
/// because inventing pixels makes a file bigger and no more useful, and a "web
/// rendition" that is a blurrier copy of the original is worse than none.
/// </summary>
public sealed partial class SkiaRenditionGenerator(ILogger<SkiaRenditionGenerator> logger) : IRenditionGenerator
{
    private const int ThumbMaxEdge = 320;
    private const int WebMaxEdge = 1600;
    private const int JpegQuality = 82;

    /// <summary>
    /// Refuse to DECODE anything above this many pixels.
    ///
    /// A 60 KB PNG can declare 40,000 x 40,000 and cost 6.4 GB of RAM the
    /// moment it is decoded — the decompression bomb. The dimensions are read
    /// from the header first and the file is rejected before a bitmap is
    /// allocated. 120 megapixels is comfortably above any real product
    /// photograph and far below the size that hurts.
    /// </summary>
    private const long MaxDecodablePixels = 120L * 1000 * 1000;

    public Task<ImageProbe?> ProbeAsync(Stream original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);

        try
        {
            using var managed = new SKManagedStream(original, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed);

            if (codec is null)
            {
                return Task.FromResult<ImageProbe?>(null);
            }

            var info = codec.Info;
            return Task.FromResult<ImageProbe?>(
                info.Width > 0 && info.Height > 0 ? new ImageProbe(info.Width, info.Height) : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A file that does not decode is not an upload failure — it is a
            // file with no dimensions. It is stored either way.
            LogProbeFailed(logger, ex);
            return Task.FromResult<ImageProbe?>(null);
        }
    }

    public Task<IReadOnlyList<GeneratedRendition>> GenerateAsync(
        Stream original, string assetType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);

        // BIM, CAD, documents and video are stored as opaque binaries by
        // decision (2026-08-19). Returning an empty list is the honest answer,
        // and the caller records only the original.
        if (!string.Equals(assetType, AssetType.Image, StringComparison.Ordinal))
        {
            return Task.FromResult<IReadOnlyList<GeneratedRendition>>([]);
        }

        try
        {
            using var managed = new SKManagedStream(original, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed);

            if (codec is null)
            {
                LogUndecodable(logger);
                return Task.FromResult<IReadOnlyList<GeneratedRendition>>([]);
            }

            var info = codec.Info;
            long pixels = (long)info.Width * info.Height;

            if (pixels <= 0 || pixels > MaxDecodablePixels)
            {
                LogTooManyPixels(logger, info.Width, info.Height, pixels);
                return Task.FromResult<IReadOnlyList<GeneratedRendition>>([]);
            }

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null)
            {
                return Task.FromResult<IReadOnlyList<GeneratedRendition>>([]);
            }

            var results = new List<GeneratedRendition>(2);

            foreach (var (role, maxEdge) in new[] { ("thumb", ThumbMaxEdge), ("web", WebMaxEdge) })
            {
                cancellationToken.ThrowIfCancellationRequested();

                var longest = Math.Max(bitmap.Width, bitmap.Height);
                if (longest <= maxEdge)
                {
                    // Already smaller than the target. No upscaling, ever.
                    continue;
                }

                var scale = (double)maxEdge / longest;
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

                using var resized = bitmap.Resize(
                    new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

                if (resized is null)
                {
                    continue;
                }

                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

                if (encoded is null)
                {
                    continue;
                }

                // Re-encoding drops EXIF with it, which is deliberate and worth
                // stating: product photography routinely carries camera serial
                // numbers and GPS coordinates, and a storefront is not the
                // place to publish either.
                results.Add(new GeneratedRendition(role, encoded.ToArray(), width, height, "jpg"));
            }

            return Task.FromResult<IReadOnlyList<GeneratedRendition>>(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The upload succeeds with the original only. Losing a thumbnail is
            // a degraded result; losing the file the user gave us is a bug.
            LogGenerationFailed(logger, ex);
            return Task.FromResult<IReadOnlyList<GeneratedRendition>>([]);
        }
    }

    [LoggerMessage(EventId = 7230, Level = LogLevel.Debug,
        Message = "Could not read image dimensions; storing without them.")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7231, Level = LogLevel.Warning,
        Message = "An IMAGE upload could not be decoded; stored without renditions.")]
    private static partial void LogUndecodable(ILogger logger);

    [LoggerMessage(EventId = 7232, Level = LogLevel.Warning,
        Message = "Refused to decode a {Width}x{Height} image ({Pixels} pixels); stored without renditions.")]
    private static partial void LogTooManyPixels(ILogger logger, int width, int height, long pixels);

    [LoggerMessage(EventId = 7233, Level = LogLevel.Warning,
        Message = "Rendition generation failed; the original was stored without derivatives.")]
    private static partial void LogGenerationFailed(ILogger logger, Exception exception);
}
