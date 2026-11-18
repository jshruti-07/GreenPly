using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Attributes;

public sealed class GetProductAttributesQueryHandler(
    IProductAttributeStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductAttributesQuery, Result<ProductAttributesDto>>
{
    public async Task<Result<ProductAttributesDto>> Handle(
        GetProductAttributesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductAttributesDto>(locale.Error);
        }

        var result = await store.GetAsync(request.Code, locale.Value, cancellationToken);

        return result is null
            ? Result.Failure<ProductAttributesDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class UpdateProductAttributesCommandHandler(
    IProductAttributeStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<UpdateProductAttributesCommand, Result<ProductAttributesDto>>
{
    public async Task<Result<ProductAttributesDto>> Handle(
        UpdateProductAttributesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<ProductAttributesDto>(locale.Error);
        }

        return await store.SaveAsync(
            request.Code, request.Values, request.RowVersion, locale.Value, cancellationToken);
    }
}

public sealed class UpdateProductAttributesCommandValidator
    : AbstractValidator<UpdateProductAttributesCommand>
{
    /// <summary>Bounds one request; the tab saves a single product's form.</summary>
    public const int MaxValuesPerRequest = 500;

    public UpdateProductAttributesCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.Values)
            .NotNull()
            .Must(v => v.Count <= MaxValuesPerRequest)
            .WithMessage($"At most {MaxValuesPerRequest} attribute values can be saved at once.");

        RuleForEach(x => x.Values).ChildRules(value =>
            value.RuleFor(v => v.AttributeCode).NotEmpty());
    }
}
