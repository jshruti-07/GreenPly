using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>Normalized, pre-validated filter set handed to the read model.</summary>
public sealed record ProductListFilter(
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending,
    WorkflowState? WorkflowState,
    long? CategoryId,
    string? Search,
    /// <summary>
    /// Locale the row text is rendered in. Already resolved against the
    /// configured locales, so the read model can use it directly — it is never
    /// user input by the time it arrives here.
    /// </summary>
    DisplayLocale DisplayLocale,
    /// <summary>Restricts the result to these product codes (CSV export of a selection).</summary>
    IReadOnlyList<string>? Codes = null);

/// <summary>
/// Read-side port for the products grid. Implemented in Infrastructure with a
/// set-based SQL query; deliberately separate from IProductRepository, which
/// stays a write-model (aggregate) concern.
/// </summary>
public interface IProductListReader
{
    Task<PagedResult<ProductListItemDto>> ListAsync(ProductListFilter filter, CancellationToken cancellationToken);
}
