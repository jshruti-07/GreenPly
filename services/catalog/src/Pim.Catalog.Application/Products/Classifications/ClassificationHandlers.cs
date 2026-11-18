using MediatR;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Classifications;

public sealed class GetProductClassificationsQueryHandler(IProductClassificationStore store)
    : IRequestHandler<GetProductClassificationsQuery, Result<ProductClassificationsDto>>
{
    public async Task<Result<ProductClassificationsDto>> Handle(
        GetProductClassificationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.GetAsync(request.Code, cancellationToken);

        return result is null
            ? Result.Failure<ProductClassificationsDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class AssignClassificationCommandHandler(IProductClassificationStore store)
    : IRequestHandler<AssignClassificationCommand, Result<ProductClassificationsDto>>
{
    public Task<Result<ProductClassificationsDto>> Handle(
        AssignClassificationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.AssignClassAsync(
            request.Code, request.SchemeCode, request.ClassCode, request.RowVersion, cancellationToken);
    }
}

public sealed class UpdateClassificationFeaturesCommandHandler(IProductClassificationStore store)
    : IRequestHandler<UpdateClassificationFeaturesCommand, Result<ProductClassificationsDto>>
{
    public Task<Result<ProductClassificationsDto>> Handle(
        UpdateClassificationFeaturesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.SaveFeaturesAsync(
            request.Code, request.SchemeCode, request.Values, request.RowVersion, cancellationToken);
    }
}
