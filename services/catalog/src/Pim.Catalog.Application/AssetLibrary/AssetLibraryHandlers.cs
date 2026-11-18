using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AssetLibrary;

public sealed class GetAssetLibrarySummaryQueryHandler(
    IAssetLibraryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetAssetLibrarySummaryQuery, Result<AssetLibrarySummaryDto>>
{
    public async Task<Result<AssetLibrarySummaryDto>> Handle(
        GetAssetLibrarySummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);

        return locale.IsFailure
            ? Result.Failure<AssetLibrarySummaryDto>(locale.Error)
            : Result.Success(await store.GetSummaryAsync(locale.Value, cancellationToken));
    }
}

public sealed class GetAssetsQueryHandler(
    IAssetLibraryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetAssetsQuery, Result<AssetPageDto>>
{
    /// <summary>Matches the products grid. One cap, one number, one place to change it.</summary>
    public const int MaxPageSize = 100;

    public async Task<Result<AssetPageDto>> Handle(
        GetAssetsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            return Result.Failure<AssetPageDto>(AssetLibraryErrors.PageOutOfRange);
        }

        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
        {
            return Result.Failure<AssetPageDto>(AssetLibraryErrors.PageSizeOutOfRange(MaxPageSize));
        }

        // The type filter is validated against what the CATALOGUE holds, not
        // against a list written here. A typo then comes back as a 400 naming
        // the problem instead of an empty grid, which on this screen would read
        // as "there are no BIM files" — a statement about the data rather than
        // about the request.
        var type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToUpperInvariant();
        if (type is not null)
        {
            var known = await store.GetKnownTypesAsync(cancellationToken);
            if (!known.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                return Result.Failure<AssetPageDto>(AssetLibraryErrors.UnknownType(request.Type!, known));
            }
        }

        var usage = NormaliseUsage(request.Usage);
        if (usage is null)
        {
            return Result.Failure<AssetPageDto>(AssetLibraryErrors.UnknownUsage(request.Usage!));
        }

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<AssetPageDto>(locale.Error);
        }

        return Result.Success(await store.GetAssetsAsync(
            type,
            usage,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            request.Page,
            request.PageSize,
            locale.Value,
            cancellationToken));
    }

    private static string? NormaliseUsage(string? usage) =>
        string.IsNullOrWhiteSpace(usage)
            ? "all"
            : usage.Trim().ToLowerInvariant() switch
            {
                "all" => "all",
                "orphan" => "orphan",
                "linked" => "linked",
                _ => null,
            };
}

public sealed class GetAssetDetailQueryHandler(
    IAssetLibraryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetAssetDetailQuery, Result<AssetDetailDto>>
{
    public async Task<Result<AssetDetailDto>> Handle(
        GetAssetDetailQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PublicId == Guid.Empty)
        {
            return Result.Failure<AssetDetailDto>(AssetLibraryErrors.IdRequired);
        }

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<AssetDetailDto>(locale.Error);
        }

        var detail = await store.GetDetailAsync(request.PublicId, locale.Value, cancellationToken);

        return detail is null
            ? Result.Failure<AssetDetailDto>(AssetLibraryErrors.NotFound(request.PublicId))
            : Result.Success(detail);
    }
}
