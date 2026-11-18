using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.CreateProduct;

/// <summary>
/// Mirrors the v4.2 create wizard: Brand, Product Family/Category (primary
/// taxonomy node), SAP Number, Product Name (base-locale master), Owner and
/// Base Language. Everything else is enriched later on the detail screen.
/// </summary>
public sealed record CreateProductCommand(
    string Code,
    long PrimaryNodeId,
    string BaseLocale,
    string Name,
    long? BrandId,
    long? AttributeSetId,
    string? Gtin,
    string? SapMaterialNo,
    string? OwnerDisplayName) : IRequest<Result<ProductDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "created";

    string IAuditableCommand.AuditDetail => $"Product created as '{Name}'";
}
