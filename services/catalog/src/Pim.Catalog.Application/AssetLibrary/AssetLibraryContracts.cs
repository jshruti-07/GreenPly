using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AssetLibrary;

/// <summary>One asset type and how much of the library it accounts for.</summary>
public sealed record AssetTypeSummaryDto(string AssetType, int Count, long TotalBytes);

/// <summary>
/// The library's headline figures.
///
/// <paramref name="OrphanCount"/> and <paramref name="ProductsWithoutMainImage"/>
/// are the two that make this screen worth opening. An asset linked to nothing
/// is storage nobody is paying attention to; a product with no main image is a
/// gap that shows up on a storefront rather than in this tool. Neither is
/// visible from any per-product screen, because both are questions about the
/// set rather than about a row.
/// </summary>
public sealed record AssetLibrarySummaryDto(
    int TotalAssets,
    long TotalBytes,
    IReadOnlyList<AssetTypeSummaryDto> ByType,
    int OrphanCount,
    int ProductsWithoutMainImage,
    int RightsExpiringSoon,
    int RightsExpired,
    IReadOnlyList<AssetOmittedPanelDto> OmittedPanels);

/// <summary>A capability v4.2 shows that this build cannot honestly provide.</summary>
public sealed record AssetOmittedPanelDto(string Panel, string Reason);

/// <summary>One card in the library grid.</summary>
public sealed record AssetListItemDto(
    Guid PublicId,
    string? FileName,
    string? Title,
    string? TitleLocale,
    string AssetType,
    string? MimeType,
    long? FileSizeBytes,
    int? Width,
    int? Height,
    string StorageProvider,
    string Status,
    // Which rendition the URL actually points at. Named rather than implied:
    // "web" and "original" are different files with different sizes, and a
    // client that cannot tell them apart will put a 4 MB original on a
    // storefront thumbnail.
    string ServedRendition,
    string Url,
    int LinkCount,
    string? UsageRights,
    DateOnly? RightsExpiry);

public sealed record AssetPageDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AssetListItemDto> Items);

/// <summary>An asset's text in one locale — present or conspicuously absent.</summary>
public sealed record AssetTextDto(
    string LocaleCode,
    string LocaleName,
    bool IsBase,
    string? Title,
    string? AltText,
    string? Caption);

/// <summary>A generated (or, today, declared) derivative of an asset.</summary>
public sealed record AssetRenditionDto(
    string Role,
    string Uri,
    int? Width,
    int? Height,
    string? Format);

/// <summary>Where an asset is used.</summary>
public sealed record AssetUsageDto(
    string ProductCode,
    string ProductName,
    string? ProductNameLocale,
    string Role,
    bool IsPrimary);

public sealed record AssetDetailDto(
    Guid PublicId,
    string? FileName,
    string? Title,
    string? TitleLocale,
    string AssetType,
    string? MimeType,
    long? FileSizeBytes,
    int? Width,
    int? Height,
    string StorageProvider,
    string StorageKey,
    string? Checksum,
    string Status,
    string? UsageRights,
    DateOnly? RightsExpiry,
    string? LocaleCode,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<AssetRenditionDto> Renditions,
    IReadOnlyList<AssetTextDto> Texts,
    IReadOnlyList<AssetUsageDto> UsedBy);

public sealed record GetAssetLibrarySummaryQuery(string? Locale = null)
    : IRequest<Result<AssetLibrarySummaryDto>>;

/// <param name="Type">Asset type filter (IMAGE, BIM, CAD, DOCUMENT, ...), or null for all.</param>
/// <param name="Usage">"all" (default), "orphan" (linked to nothing), or "linked".</param>
public sealed record GetAssetsQuery(
    string? Type = null,
    string? Usage = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 24,
    string? Locale = null)
    : IRequest<Result<AssetPageDto>>;

public sealed record GetAssetDetailQuery(Guid PublicId, string? Locale = null)
    : IRequest<Result<AssetDetailDto>>;

public interface IAssetLibraryStore
{
    Task<AssetLibrarySummaryDto> GetSummaryAsync(
        DisplayLocale locale, CancellationToken cancellationToken);

    Task<AssetPageDto> GetAssetsAsync(
        string? type, string usage, string? search, int page, int pageSize,
        DisplayLocale locale, CancellationToken cancellationToken);

    /// <summary>Null when no asset carries that id.</summary>
    Task<AssetDetailDto?> GetDetailAsync(
        Guid publicId, DisplayLocale locale, CancellationToken cancellationToken);

    /// <summary>The asset types the catalogue actually holds, for the facet row.</summary>
    Task<IReadOnlyList<string>> GetKnownTypesAsync(CancellationToken cancellationToken);
}
