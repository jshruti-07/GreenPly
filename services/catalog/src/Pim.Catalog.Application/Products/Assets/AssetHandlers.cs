using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Assets;

// Every handler here resolves a display locale before touching the store
// (R-18). For the read that is the usual ADR-009 rule. For the writes it is
// more than presentation: the resolved locale is the one asset_tr rows are
// written to, so an unconfigured locale must be rejected before anything is
// persisted rather than after.

public sealed class GetProductAssetsQueryHandler(
    IProductAssetStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductAssetsQuery, Result<ProductAssetsDto>>
{
    public async Task<Result<ProductAssetsDto>> Handle(
        GetProductAssetsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductAssetsDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);
        return result is null
            ? Result.Failure<ProductAssetsDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class AddProductAssetCommandHandler(
    IProductAssetStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<AddProductAssetCommand, Result<ProductAssetsDto>>
{
    public async Task<Result<ProductAssetsDto>> Handle(
        AddProductAssetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductAssetsDto>(locale.Error)
            : await store.AddAsync(request, locale.Value, cancellationToken);
    }
}

public sealed class UpdateProductAssetCommandHandler(
    IProductAssetStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<UpdateProductAssetCommand, Result<ProductAssetsDto>>
{
    public async Task<Result<ProductAssetsDto>> Handle(
        UpdateProductAssetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductAssetsDto>(locale.Error)
            : await store.UpdateAsync(request, locale.Value, cancellationToken);
    }
}

public sealed class SetPrimaryAssetCommandHandler(
    IProductAssetStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<SetPrimaryAssetCommand, Result<ProductAssetsDto>>
{
    public async Task<Result<ProductAssetsDto>> Handle(
        SetPrimaryAssetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Writes no text, so the locale only governs the payload that comes
        // back. Carried anyway: a tab that changed language when you starred an
        // image would be no less jarring for the write being unrelated.
        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductAssetsDto>(locale.Error)
            : await store.SetPrimaryAsync(request, locale.Value, cancellationToken);
    }
}

public sealed class RemoveProductAssetCommandHandler(
    IProductAssetStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<RemoveProductAssetCommand, Result<ProductAssetsDto>>
{
    public async Task<Result<ProductAssetsDto>> Handle(
        RemoveProductAssetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductAssetsDto>(locale.Error)
            : await store.RemoveAsync(request, locale.Value, cancellationToken);
    }
}
