using MediatR;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.UpdateProductGeneral;

public sealed partial class UpdateProductGeneralCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    ILogger<UpdateProductGeneralCommandHandler> logger)
    : IRequestHandler<UpdateProductGeneralCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        UpdateProductGeneralCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductDto>(codeResult.Error);
        }

        var product = await productRepository.GetByCodeAsync(codeResult.Value, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductDto>(ProductErrors.NotFound(codeResult.Value.Value));
        }

        // Checked in the domain before any mutation so a stale edit changes nothing.
        var updateResult = product.UpdateGeneral(
            request.Name,
            request.OwnerDisplayName,
            request.Gtin,
            request.SapMaterialNo,
            request.RowVersion);

        if (updateResult.IsFailure)
        {
            return Result.Failure<ProductDto>(updateResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.ProductUpdated(logger, product.Code.Value, product.RowVersion);

        return ProductDto.FromDomain(product);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Product {ProductCode} general fields updated to row version {RowVersion}")]
        public static partial void ProductUpdated(ILogger logger, string productCode, int rowVersion);
    }
}
