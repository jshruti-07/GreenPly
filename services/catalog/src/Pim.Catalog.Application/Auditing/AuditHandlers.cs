using MediatR;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Auditing;

public sealed class GetProductActivityQueryHandler(IProductActivityReader reader)
    : IRequestHandler<GetProductActivityQuery, Result<ProductActivityDto>>
{
    public async Task<Result<ProductActivityDto>> Handle(
        GetProductActivityQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await reader.GetAsync(request, cancellationToken);

        return result is null
            ? Result.Failure<ProductActivityDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}
