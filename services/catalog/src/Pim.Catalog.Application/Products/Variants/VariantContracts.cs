using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Variants;

public sealed record SkuDto(
    string Code,
    string? Gtin,
    string? ErpMaterialNo,
    string Status,
    int SortOrder);

/// <summary>The Variants tab payload. RowVersion is the product's.</summary>
public sealed record ProductVariantsDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<SkuDto> Skus);

public sealed record GetProductVariantsQuery(string Code) : IRequest<Result<ProductVariantsDto>>;

public sealed record AddSkuCommand(
    string ProductCode,
    string Code,
    string? Gtin,
    string? ErpMaterialNo,
    int RowVersion) : IRequest<Result<ProductVariantsDto>>, IAuditableCommand
{
    // ProductCode above already satisfies IAuditableCommand.
    string IAuditableCommand.AuditAction => "created";

    string IAuditableCommand.AuditDetail => $"Variant {Code} added";
}

public sealed record UpdateSkuCommand(
    string ProductCode,
    string Code,
    string? Gtin,
    string? ErpMaterialNo,
    string Status,
    int RowVersion) : IRequest<Result<ProductVariantsDto>>, IAuditableCommand
{
    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Variant {Code} set to {Status}";
}

public sealed record RemoveSkuCommand(
    string ProductCode,
    string Code,
    int RowVersion) : IRequest<Result<ProductVariantsDto>>, IAuditableCommand
{
    string IAuditableCommand.AuditAction => "deleted";

    string IAuditableCommand.AuditDetail => $"Variant {Code} removed";
}

public sealed class AddSkuCommandValidator : AbstractValidator<AddSkuCommand>
{
    public AddSkuCommandValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Sku.MaxCodeLength);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

public sealed class UpdateSkuCommandValidator : AbstractValidator<UpdateSkuCommand>
{
    public UpdateSkuCommandValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<SkuStatus>(s, ignoreCase: true, out _))
            .WithMessage(x =>
                $"Unknown SKU status '{x.Status}'. Allowed: {string.Join(", ", Enum.GetNames<SkuStatus>())}.");
    }
}

public sealed class RemoveSkuCommandValidator : AbstractValidator<RemoveSkuCommand>
{
    public RemoveSkuCommandValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}
