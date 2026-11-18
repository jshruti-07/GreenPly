using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.UpdateProductGeneral;

/// <summary>
/// Edits the General tab's editable fields. Code, taxonomy node and base
/// locale are deliberately NOT editable here: changing them re-keys or
/// re-classifies the product, which is a separate, audited operation.
///
/// <paramref name="RowVersion"/> carries the value the client loaded, so a
/// concurrent edit is rejected rather than silently overwritten.
/// </summary>
public sealed record UpdateProductGeneralCommand(
    string Code,
    string Name,
    string? OwnerDisplayName,
    string? Gtin,
    string? SapMaterialNo,
    int RowVersion) : IRequest<Result<ProductDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => "General information";
}

public sealed class UpdateProductGeneralCommandValidator : AbstractValidator<UpdateProductGeneralCommand>
{
    public UpdateProductGeneralCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(ProductCode.MaxLength);

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

        RuleFor(x => x.SapMaterialNo)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.SapMaterialNo));

        RuleFor(x => x.RowVersion)
            .GreaterThan(0);
    }
}
