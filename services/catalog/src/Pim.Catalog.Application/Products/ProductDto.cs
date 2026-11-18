using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Application.Products;

public sealed record ProductDto(
    Guid PublicId,
    string Code,
    string? Name,
    long PrimaryNodeId,
    long? BrandId,
    string BaseLocale,
    string LifecycleState,
    string WorkflowState,
    string? Gtin,
    string? SapMaterialNo,
    string? OwnerDisplayName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    /// <summary>Send this back on update; a mismatch means someone else saved first.</summary>
    int RowVersion)
{
    public static ProductDto FromDomain(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductDto(
            product.PublicId,
            product.Code.Value,
            product.BaseLocaleName,
            product.PrimaryNodeId,
            product.BrandId,
            product.BaseLocale,
            product.LifecycleState.ToString(),
            product.WorkflowState.ToString(),
            product.Gtin,
            product.SapMaterialNo,
            product.OwnerDisplayName,
            product.CreatedAtUtc,
            product.UpdatedAtUtc,
            product.RowVersion);
    }
}
