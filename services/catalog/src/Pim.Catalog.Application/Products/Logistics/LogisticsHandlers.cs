using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Logistics;

public sealed class GetProductPackagingQueryHandler(IProductPackagingStore store)
    : IRequestHandler<GetProductPackagingQuery, Result<ProductPackagingDto>>
{
    public async Task<Result<ProductPackagingDto>> Handle(
        GetProductPackagingQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.GetAsync(request.Code, cancellationToken);
        return result is null
            ? Result.Failure<ProductPackagingDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class AddPackagingUnitCommandHandler(IProductPackagingStore store)
    : IRequestHandler<AddPackagingUnitCommand, Result<ProductPackagingDto>>
{
    public Task<Result<ProductPackagingDto>> Handle(
        AddPackagingUnitCommand request, CancellationToken cancellationToken) =>
        store.AddAsync(request, cancellationToken);
}

public sealed class UpdatePackagingUnitCommandHandler(IProductPackagingStore store)
    : IRequestHandler<UpdatePackagingUnitCommand, Result<ProductPackagingDto>>
{
    public Task<Result<ProductPackagingDto>> Handle(
        UpdatePackagingUnitCommand request, CancellationToken cancellationToken) =>
        store.UpdateAsync(request, cancellationToken);
}

public sealed class RemovePackagingUnitCommandHandler(IProductPackagingStore store)
    : IRequestHandler<RemovePackagingUnitCommand, Result<ProductPackagingDto>>
{
    public Task<Result<ProductPackagingDto>> Handle(
        RemovePackagingUnitCommand request, CancellationToken cancellationToken) =>
        store.RemoveAsync(request, cancellationToken);
}

// The four handlers below resolve a display locale before touching their
// store (ADR-009, R-15 Phase 2). Packaging above deliberately does not: its
// payload is dimensions and weights, and there is no translated text on it to
// render in anyone's language.

public sealed class GetProductRelationshipsQueryHandler(
    IProductRelationshipStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductRelationshipsQuery, Result<ProductRelationshipsDto>>
{
    public async Task<Result<ProductRelationshipsDto>> Handle(
        GetProductRelationshipsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductRelationshipsDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);
        return result is null
            ? Result.Failure<ProductRelationshipsDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class AddRelationshipCommandHandler(
    IProductRelationshipStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<AddRelationshipCommand, Result<ProductRelationshipsDto>>
{
    public async Task<Result<ProductRelationshipsDto>> Handle(
        AddRelationshipCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductRelationshipsDto>(locale.Error)
            : await store.AddAsync(request, locale.Value, cancellationToken);
    }
}

public sealed class RemoveRelationshipCommandHandler(
    IProductRelationshipStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<RemoveRelationshipCommand, Result<ProductRelationshipsDto>>
{
    public async Task<Result<ProductRelationshipsDto>> Handle(
        RemoveRelationshipCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductRelationshipsDto>(locale.Error)
            : await store.RemoveAsync(request, locale.Value, cancellationToken);
    }
}

public sealed class GetProductApplicationsQueryHandler(
    IProductApplicationStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductApplicationsQuery, Result<ProductApplicationsDto>>
{
    public async Task<Result<ProductApplicationsDto>> Handle(
        GetProductApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductApplicationsDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);
        return result is null
            ? Result.Failure<ProductApplicationsDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class SetProductApplicationsCommandHandler(
    IProductApplicationStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<SetProductApplicationsCommand, Result<ProductApplicationsDto>>
{
    public async Task<Result<ProductApplicationsDto>> Handle(
        SetProductApplicationsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductApplicationsDto>(locale.Error)
            : await store.SetAsync(request, locale.Value, cancellationToken);
    }
}
