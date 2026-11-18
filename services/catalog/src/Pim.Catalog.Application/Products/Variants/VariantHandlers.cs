using MediatR;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Variants;

/// <summary>
/// All four handlers share the same shape: resolve the product, mutate through
/// the aggregate (which owns the concurrency and uniqueness rules), save, and
/// return the refreshed variant list so the client never needs a second call.
/// </summary>
internal static class VariantMapper
{
    public static ProductVariantsDto ToDto(Product product) => new(
        product.Code.Value,
        product.RowVersion,
        [.. product.Skus
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s => new SkuDto(
                s.Code,
                s.Gtin,
                s.ErpMaterialNo,
                s.Status.ToString(),
                s.SortOrder))]);
}

public sealed class GetProductVariantsQueryHandler(IProductRepository repository)
    : IRequestHandler<GetProductVariantsQuery, Result<ProductVariantsDto>>
{
    public async Task<Result<ProductVariantsDto>> Handle(
        GetProductVariantsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(codeResult.Error);
        }

        var product = await repository.GetByCodeAsync(codeResult.Value, cancellationToken);
        return product is null
            ? Result.Failure<ProductVariantsDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(VariantMapper.ToDto(product));
    }
}

public sealed class AddSkuCommandHandler(IProductRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<AddSkuCommand, Result<ProductVariantsDto>>
{
    public async Task<Result<ProductVariantsDto>> Handle(
        AddSkuCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await VariantHandlerSupport.LoadAsync(repository, request.ProductCode, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(loaded.Error);
        }

        var product = loaded.Value;
        var added = product.AddSku(request.Code, request.Gtin, request.ErpMaterialNo, request.RowVersion);
        if (added.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(added.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return VariantMapper.ToDto(product);
    }
}

public sealed class UpdateSkuCommandHandler(IProductRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateSkuCommand, Result<ProductVariantsDto>>
{
    public async Task<Result<ProductVariantsDto>> Handle(
        UpdateSkuCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await VariantHandlerSupport.LoadAsync(repository, request.ProductCode, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(loaded.Error);
        }

        var product = loaded.Value;
        var status = Enum.Parse<SkuStatus>(request.Status, ignoreCase: true);

        var updated = product.UpdateSku(
            request.Code, request.Gtin, request.ErpMaterialNo, status, request.RowVersion);

        if (updated.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(updated.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return VariantMapper.ToDto(product);
    }
}

public sealed class RemoveSkuCommandHandler(IProductRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveSkuCommand, Result<ProductVariantsDto>>
{
    public async Task<Result<ProductVariantsDto>> Handle(
        RemoveSkuCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await VariantHandlerSupport.LoadAsync(repository, request.ProductCode, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(loaded.Error);
        }

        var product = loaded.Value;
        var removed = product.RemoveSku(request.Code, request.RowVersion);
        if (removed.IsFailure)
        {
            return Result.Failure<ProductVariantsDto>(removed.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return VariantMapper.ToDto(product);
    }
}

internal static class VariantHandlerSupport
{
    public static async Task<Result<Product>> LoadAsync(
        IProductRepository repository,
        string productCode,
        CancellationToken cancellationToken)
    {
        var codeResult = ProductCode.Create(productCode);
        if (codeResult.IsFailure)
        {
            return Result.Failure<Product>(codeResult.Error);
        }

        var product = await repository.GetByCodeAsync(codeResult.Value, cancellationToken);
        return product is null
            ? Result.Failure<Product>(ProductErrors.NotFound(productCode))
            : Result.Success(product);
    }
}
