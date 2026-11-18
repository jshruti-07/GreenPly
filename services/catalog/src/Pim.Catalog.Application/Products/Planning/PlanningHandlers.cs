using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Planning;

public sealed class GetProductPlanningQueryHandler(
    IProductPlanningStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductPlanningQuery, Result<ProductPlanningDto>>
{
    public async Task<Result<ProductPlanningDto>> Handle(
        GetProductPlanningQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductPlanningDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);
        return result is null
            ? Result.Failure<ProductPlanningDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}
