using Microsoft.EntityFrameworkCore;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.Repositories;

public sealed class ProductRepository(CatalogDbContext dbContext) : IProductRepository
{
    public Task<Product?> GetByCodeAsync(ProductCode code, CancellationToken cancellationToken = default) =>
        // Translations are owned by the aggregate, so EF includes them automatically.
        dbContext.Products.FirstOrDefaultAsync(p => p.Code == code, cancellationToken);

    public Task<bool> ExistsByCodeAsync(ProductCode code, CancellationToken cancellationToken = default) =>
        dbContext.Products.AnyAsync(p => p.Code == code, cancellationToken);

    public void Add(Product product) => dbContext.Products.Add(product);
}
