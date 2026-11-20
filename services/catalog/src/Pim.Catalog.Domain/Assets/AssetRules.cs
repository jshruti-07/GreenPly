using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Assets;

/// <summary>
/// What a file IS. Note DOCUMENT sits alongside IMAGE rather than in a module
/// of its own: a datasheet and a product photo need identical linking, rights
/// and versioning, and splitting them would duplicate all of it.
/// </summary>
public static class AssetType
{
    public const string Image = "IMAGE";
    public const string Cad = "CAD";
    public const string Bim = "BIM";
    public const string Document = "DOCUMENT";
    public const string Video = "VIDEO";
    public const string Spin360 = "SPIN_360";

    public static readonly string[] All = [Image, Cad, Bim, Document, Video, Spin360];

    public static bool IsKnown(string value) =>
        Array.Exists(All, t => string.Equals(t, value, StringComparison.Ordinal));

    /// <summary>Types the Media half of the tab renders as pictures.</summary>
    public static bool IsVisual(string value) =>
        value is Image or Spin360;
}

/// <summary>
/// What a file is FOR on this product. Type and role are independent: a PDF can
/// be a DATASHEET or an INSTALL guide, and knowing it is a PDF does not tell a
/// channel which one to publish.
/// </summary>
public static class AssetRole
{
    public const string Main = "MAIN";
    public const string Gallery = "GALLERY";
    public const string Datasheet = "DATASHEET";
    public const string Install = "INSTALL";
    public const string Drawing = "DRAWING";
    public const string Bim = "BIM";
    public const string Video = "VIDEO";
    public const string Spin = "SPIN";

    public static readonly string[] All =
        [Main, Gallery, Datasheet, Install, Drawing, Bim, Video, Spin];

    public static bool IsKnown(string value) =>
        Array.Exists(All, r => string.Equals(r, value, StringComparison.Ordinal));

    /// <summary>
    /// MAIN is the one image a channel picks when it can only show one, so a
    /// product may have at most one and it is always primary.
    /// </summary>
    public static bool IsSingular(string role) =>
        string.Equals(role, Main, StringComparison.Ordinal);
}

public static class AssetErrors
{
    public static Error NotFound(long id) =>
        Error.NotFound("Asset.NotFound", $"Asset link {id} was not found on this product.");

    public static Error UnknownType(string value) =>
        Error.Validation("Asset.UnknownType",
            $"'{value}' is not a recognised asset type. Expected one of: {string.Join(", ", AssetType.All)}.");

    public static Error UnknownRole(string value) =>
        Error.Validation("Asset.UnknownRole",
            $"'{value}' is not a recognised asset role. Expected one of: {string.Join(", ", AssetRole.All)}.");

    public static Error AlreadyLinked(string role) =>
        Error.Conflict("Asset.AlreadyLinked",
            $"This asset is already linked to the product as {role}.");

    public static Error MainAlreadySet(string reference) =>
        Error.Conflict("Asset.MainAlreadySet",
            $"This product already has a main image ({reference}). Replace it or add this one to the gallery instead.");

    public static readonly Error StorageKeyRequired =
        Error.Validation("Asset.StorageKeyRequired",
            "A storage key or URL is required to register an asset.");

    public static readonly Error StorageKeyTooLong =
        Error.Validation("Asset.StorageKeyTooLong",
            "The storage key exceeds 2048 characters.");

    public static Error TypeRoleMismatch(string type, string role) =>
        Error.Validation("Asset.TypeRoleMismatch",
            $"A {type} asset cannot take the {role} role.");
}

public static class AssetRules
{
    public const int MaxStorageKeyLength = 2048;

    /// <summary>
    /// Roles a given asset type may take.
    ///
    /// Enforced because the pairing is what channels rely on: a feed asking for
    /// the MAIN image and receiving a DXF drawing has no way to recover, and
    /// the mistake is invisible in the PIM until something downstream breaks.
    /// </summary>
    public static Result Validate(string assetType, string role)
    {
        if (!AssetType.IsKnown(assetType))
        {
            return Result.Failure(AssetErrors.UnknownType(assetType));
        }

        if (!AssetRole.IsKnown(role))
        {
            return Result.Failure(AssetErrors.UnknownRole(role));
        }

        var allowed = assetType switch
        {
            AssetType.Image => role is AssetRole.Main or AssetRole.Gallery,
            AssetType.Spin360 => role is AssetRole.Spin or AssetRole.Gallery,
            AssetType.Video => role is AssetRole.Video,
            AssetType.Bim => role is AssetRole.Bim,
            AssetType.Cad => role is AssetRole.Drawing,
            AssetType.Document => role is AssetRole.Datasheet or AssetRole.Install,
            _ => false,
        };

        return allowed
            ? Result.Success()
            : Result.Failure(AssetErrors.TypeRoleMismatch(assetType, role));
    }

    /// <summary>
    /// Guesses the asset type from a file name so a steward pasting a URL does
    /// not also have to classify it. Only a default — the caller may override.
    /// </summary>
    public static string InferType(string fileNameOrKey, string? role = null)
    {
        var value = fileNameOrKey.ToUpperInvariant();

        if (EndsWithAny(value, ".JPG", ".JPEG", ".PNG", ".WEBP", ".GIF", ".TIF", ".TIFF", ".SVG", ".AVIF", ".BMP", ".ICO")
            || value.StartsWith("DATA:IMAGE/", StringComparison.Ordinal)
            || ContainsAny(value, ".JPG?", ".JPEG?", ".PNG?", ".WEBP?", ".GIF?", ".SVG?"))
        {
            return AssetType.Image;
        }

        if (EndsWithAny(value, ".PDF", ".DOC", ".DOCX", ".XLS", ".XLSX", ".TXT")
            || ContainsAny(value, ".PDF?", ".DOC?", ".DOCX?"))
        {
            return AssetType.Document;
        }

        if (EndsWithAny(value, ".IFC", ".RVT", ".RFA"))
        {
            return AssetType.Bim;
        }

        if (EndsWithAny(value, ".DWG", ".DXF", ".STEP", ".STP"))
        {
            return AssetType.Cad;
        }

        if (EndsWithAny(value, ".MP4", ".MOV", ".WEBM")
            || ContainsAny(value, ".MP4?", ".MOV?", ".WEBM?"))
        {
            return AssetType.Video;
        }

        // When the file name/URL does not have a recognizable file extension,
        // use the intended role to infer the type.
        // A MAIN or GALLERY role is visual (IMAGE).
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (role is AssetRole.Main or AssetRole.Gallery)
            {
                return AssetType.Image;
            }

            if (role is AssetRole.Video)
            {
                return AssetType.Video;
            }

            if (role is AssetRole.Spin)
            {
                return AssetType.Spin360;
            }

            if (role is AssetRole.Drawing)
            {
                return AssetType.Cad;
            }

            if (role is AssetRole.Bim)
            {
                return AssetType.Bim;
            }

            if (role is AssetRole.Datasheet or AssetRole.Install)
            {
                return AssetType.Document;
            }
        }

        return AssetType.Document;
    }

    /// <summary>Best-effort MIME from the extension; null when unrecognised.</summary>
    public static string? InferMimeType(string fileNameOrKey, string? assetType = null)
    {
        var value = fileNameOrKey.ToUpperInvariant();

        if (EndsWithAny(value, ".JPG", ".JPEG") || ContainsAny(value, ".JPG?", ".JPEG?")) return "image/jpeg";
        if (EndsWithAny(value, ".PNG") || ContainsAny(value, ".PNG?")) return "image/png";
        if (EndsWithAny(value, ".WEBP") || ContainsAny(value, ".WEBP?")) return "image/webp";
        if (EndsWithAny(value, ".GIF") || ContainsAny(value, ".GIF?")) return "image/gif";
        if (EndsWithAny(value, ".SVG") || ContainsAny(value, ".SVG?")) return "image/svg+xml";
        if (EndsWithAny(value, ".PDF") || ContainsAny(value, ".PDF?")) return "application/pdf";
        if (EndsWithAny(value, ".DXF") || ContainsAny(value, ".DXF?")) return "image/vnd.dxf";
        if (EndsWithAny(value, ".IFC") || ContainsAny(value, ".IFC?")) return "application/octet-stream";
        if (EndsWithAny(value, ".MP4") || ContainsAny(value, ".MP4?")) return "video/mp4";

        if (string.Equals(assetType, AssetType.Image, StringComparison.Ordinal)) return "image/jpeg";
        if (string.Equals(assetType, AssetType.Document, StringComparison.Ordinal)) return "application/pdf";
        if (string.Equals(assetType, AssetType.Video, StringComparison.Ordinal)) return "video/mp4";

        return null;
    }

    /// <summary>
    /// The provider is derived from the key rather than accepted from the
    /// client: whether a binary lives on the client's server or ours is a fact
    /// about the key, not a caller's opinion.
    /// </summary>
    public static string InferProvider(string storageKey) =>
        storageKey.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || storageKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "external"
            : "local";

    public static string FileNameFrom(string storageKey)
    {
        var trimmed = storageKey.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        var query = name.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? name[..query] : name;
    }

    private static bool EndsWithAny(string value, params string[] suffixes) =>
        Array.Exists(suffixes, s => value.EndsWith(s, StringComparison.Ordinal));

    private static bool ContainsAny(string value, params string[] substrings) =>
        Array.Exists(substrings, s => value.Contains(s, StringComparison.Ordinal));
}
