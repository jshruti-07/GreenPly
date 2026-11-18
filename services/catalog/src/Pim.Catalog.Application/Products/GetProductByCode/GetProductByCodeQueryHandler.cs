using MediatR;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.GetProductByCode;

public sealed class GetProductByCodeQueryHandler(IProductRepository productRepository)
    : IRequestHandler<GetProductByCodeQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(GetProductByCodeQuery request, CancellationToken cancellationToken)
    {
        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductDto>(codeResult.Error);
        }

        var product = await productRepository.GetByCodeAsync(codeResult.Value, cancellationToken);

        return product is null
            ? Result.Failure<ProductDto>(ProductErrors.NotFound(codeResult.Value.Value))
            : ProductDto.FromDomain(product);
    }
}
