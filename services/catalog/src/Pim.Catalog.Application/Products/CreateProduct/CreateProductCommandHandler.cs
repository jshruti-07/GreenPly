using MediatR;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.CreateProduct;

public sealed partial class CreateProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    ILogger<CreateProductCommandHandler> logger)
    : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductDto>(codeResult.Error);
        }

        // Pre-check for a friendly error; the unique index on product_code
        // remains the authoritative guard against races.
        if (await productRepository.ExistsByCodeAsync(codeResult.Value, cancellationToken))
        {
            return Result.Failure<ProductDto>(ProductErrors.DuplicateCode);
        }

        var productResult = Product.Create(
            codeResult.Value,
            request.PrimaryNodeId,
            request.BaseLocale,
            request.Name,
            request.BrandId,
            request.AttributeSetId,
            request.Gtin,
            request.SapMaterialNo,
            request.OwnerDisplayName);

        if (productResult.IsFailure)
        {
            return Result.Failure<ProductDto>(productResult.Error);
        }

        productRepository.Add(productResult.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.ProductCreated(logger, productResult.Value.Code.Value, productResult.Value.PublicId);

        return ProductDto.FromDomain(productResult.Value);
    }

    /// <summary>Source-generated logging (CA1848/CA1873-compliant).</summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Product {ProductCode} created with public id {PublicId}")]
        public static partial void ProductCreated(ILogger logger, string productCode, Guid publicId);
    }
}
