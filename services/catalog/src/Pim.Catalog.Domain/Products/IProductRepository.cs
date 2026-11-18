namespace Pim.Catalog.Domain.Products;

/// <summary>Aggregate persistence port; implemented in Pim.Catalog.Infrastructure.</summary>
public interface IProductRepository
{
    Task<Product?> GetByCodeAsync(ProductCode code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(ProductCode code, CancellationToken cancellationToken = default);

    void Add(Product product);
}
