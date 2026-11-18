using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Assets;

/// <summary>
/// What may be uploaded, and how much of it.
///
/// In Domain beside <see cref="AssetRules"/> because these are statements about
/// what an asset IS, not about how it is transported. The extension allow-list
/// is deliberately a list rather than a "reject these" list: a deny-list on
/// file upload is a promise you cannot keep, because the next dangerous
/// extension is the one nobody has added yet.
/// </summary>
public static class AssetUploadRules
{
    /// <summary>
    /// Per-type ceilings, in bytes. Chosen for the shapes this catalogue
    /// actually holds: photography runs to tens of megabytes, a Revit family
    /// runs to hundreds. They are ceilings against accident and abuse, not
    /// quotas — nothing here bills by the byte.
    /// </summary>
    public static long MaxBytesFor(string assetType) => assetType switch
    {
        AssetType.Image => 50L * 1024 * 1024,
        AssetType.Document => 100L * 1024 * 1024,
        AssetType.Bim or AssetType.Cad => 500L * 1024 * 1024,
        AssetType.Video or AssetType.Spin360 => 1024L * 1024 * 1024,
        _ => 50L * 1024 * 1024,
    };

    /// <summary>
    /// Extensions accepted per type. An upload whose extension does not match
    /// the declared type is rejected rather than silently reclassified: a .exe
    /// declared as an IMAGE is not a mistake to be helpfully corrected.
    /// </summary>
    public static IReadOnlyList<string> ExtensionsFor(string assetType) => assetType switch
    {
        AssetType.Image => [".jpg", ".jpeg", ".png", ".webp", ".gif", ".tif", ".tiff"],
        AssetType.Document => [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv"],
        AssetType.Bim => [".ifc", ".ifczip", ".rvt", ".rfa"],
        AssetType.Cad => [".dwg", ".dxf", ".step", ".stp", ".iges", ".igs"],
        AssetType.Video => [".mp4", ".mov", ".webm"],
        AssetType.Spin360 => [".zip", ".mp4"],
        _ => [],
    };

    /// <summary>
    /// Validates a candidate upload. Size is checked by the caller as it
    /// streams — Content-Length is a claim, not a measurement — so this takes
    /// the byte count actually read.
    /// </summary>
    public static Result Validate(string assetType, string fileName, long byteCount)
    {
        if (!AssetType.IsKnown(assetType))
        {
            return Result.Failure(AssetErrors.UnknownType(assetType));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure(AssetUploadErrors.FileNameRequired);
        }

        // A file name arriving over HTTP is attacker-controlled. It is never
        // used to build a storage path (keys are content-addressed and
        // generated), but it is stored and later displayed, so separators and
        // traversal segments are refused rather than sanitised — quiet
        // sanitising makes the stored name differ from the one the user sees.
        if (fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return Result.Failure(AssetUploadErrors.FileNameUnsafe);
        }

        if (byteCount <= 0)
        {
            return Result.Failure(AssetUploadErrors.Empty);
        }

        var max = MaxBytesFor(assetType);
        if (byteCount > max)
        {
            return Result.Failure(AssetUploadErrors.TooLarge(assetType, byteCount, max));
        }

        var allowed = ExtensionsFor(assetType);
        var extension = ExtensionOf(fileName);

        if (extension.Length == 0 || !allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure(AssetUploadErrors.ExtensionNotAllowed(assetType, extension));
        }

        return Result.Success();
    }

    /// <summary>Lower-case extension including the dot, or empty when there is none.</summary>
    public static string ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 || dot == fileName.Length - 1
            ? string.Empty
            : fileName[dot..].ToLowerInvariant();
    }
}

public static class AssetUploadErrors
{
    public static readonly Error FileNameRequired =
        Error.Validation("Asset.Upload.FileNameRequired", "A file name is required.");

    public static readonly Error FileNameUnsafe =
        Error.Validation("Asset.Upload.FileNameUnsafe",
            "A file name must not contain path separators or '..'.");

    public static readonly Error Empty =
        Error.Validation("Asset.Upload.Empty", "The uploaded file is empty.");

    public static readonly Error FileRequired =
        Error.Validation("Asset.Upload.FileRequired", "A file is required.");

    public static Error TooLarge(string assetType, long actual, long max) =>
        Error.Validation("Asset.Upload.TooLarge",
            $"A {assetType} upload may be at most {max / (1024 * 1024)} MB; this one is {actual / (1024 * 1024)} MB.");

    /// <summary>
    /// The accepted list is looked up rather than passed in, and every input is
    /// null-tolerant. ErrorTaxonomyTests invokes every error factory
    /// reflectively with default arguments to check the taxonomy, so a factory
    /// that dereferences its parameters takes the whole suite down with a
    /// NullReferenceException that says nothing about the taxonomy.
    /// </summary>
    public static Error ExtensionNotAllowed(string assetType, string extension) =>
        Error.Validation("Asset.Upload.ExtensionNotAllowed",
            $"'{(string.IsNullOrEmpty(extension) ? "(none)" : extension)}' is not accepted for "
            + $"{(string.IsNullOrEmpty(assetType) ? "this asset type" : assetType)}. "
            + $"Accepted: {string.Join(", ", AssetUploadRules.ExtensionsFor(assetType ?? string.Empty))}.");

    public static Error NotStoredHere(string provider) =>
        Error.Validation("Asset.File.NotStoredHere",
            $"This asset's binary lives in '{provider}', not in this service, so it cannot be streamed from here.");

    public static Error RenditionNotFound(string role) =>
        Error.NotFound("Asset.Rendition.NotFound", $"This asset has no '{role}' rendition.");
}
