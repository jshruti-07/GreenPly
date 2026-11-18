using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AssetLibrary;

public static class AssetLibraryErrors
{
    public static readonly Error IdRequired =
        Error.Validation("Asset.Id.Required", "An asset id is required.");

    public static Error NotFound(Guid publicId) =>
        Error.NotFound("Asset.NotFound", $"Asset '{publicId}' was not found.");

    /// <summary>
    /// Names what the catalogue actually holds. An error that says "unknown
    /// type" without saying which types exist sends the reader to the schema.
    /// </summary>
    public static Error UnknownType(string type, IReadOnlyList<string> known) =>
        Error.Validation("Asset.Type.Unknown",
            $"'{type}' is not an asset type in this catalogue. Known types: {string.Join(", ", known)}.");

    public static Error UnknownUsage(string usage) =>
        Error.Validation("Asset.Usage.Unknown",
            $"'{usage}' is not a usage filter. Use all, orphan or linked.");

    public static readonly Error PageOutOfRange =
        Error.Validation("Asset.Page.OutOfRange", "Page must be 1 or greater.");

    public static Error PageSizeOutOfRange(int max) =>
        Error.Validation("Asset.PageSize.OutOfRange", $"Page size must be between 1 and {max}.");
}
