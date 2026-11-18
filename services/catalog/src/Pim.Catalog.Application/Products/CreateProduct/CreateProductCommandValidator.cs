using FluentValidation;
using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Application.Products.CreateProduct;

/// <summary>
/// Input-shape validation only. Business rules (code format, lifecycle) live
/// in the domain; uniqueness is enforced by the handler + database constraint.
/// </summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(ProductCode.MaxLength);

        RuleFor(x => x.PrimaryNodeId)
            .GreaterThan(0);

        RuleFor(x => x.BaseLocale)
            .NotEmpty()
            .Matches("^[a-z]{2}(-[A-Z]{2})?$")
            .WithMessage("Base locale must be an ISO code such as 'en' or 'en-GB'.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ProductTranslation.MaxNameLength);

        RuleFor(x => x.OwnerDisplayName)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.OwnerDisplayName));

        RuleFor(x => x.Gtin)
            .Matches("^[0-9]{8,14}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Gtin))
            .WithMessage("GTIN must be 8-14 digits.");
    }
}
