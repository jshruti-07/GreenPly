using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Localization;

public sealed class GetProductLocalizationQueryHandler(
    IProductLocalizationStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductLocalizationQuery, Result<ProductLocalizationDto>>
{
    public async Task<Result<ProductLocalizationDto>> Handle(
        GetProductLocalizationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Resolves the language the tab is READ in. Which locales it offers to
        // edit is a different question entirely, answered by catalog.locale.
        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductLocalizationDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);
        return result is null
            ? Result.Failure<ProductLocalizationDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class UpdateProductLocalizationCommandHandler(
    IProductLocalizationStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<UpdateProductLocalizationCommand, Result<ProductLocalizationDto>>
{
    public async Task<Result<ProductLocalizationDto>> Handle(
        UpdateProductLocalizationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.DisplayLocale, cancellationToken);
        return locale.IsFailure
            ? Result.Failure<ProductLocalizationDto>(locale.Error)
            : await store.SaveAsync(request, locale.Value, cancellationToken);
    }
}
