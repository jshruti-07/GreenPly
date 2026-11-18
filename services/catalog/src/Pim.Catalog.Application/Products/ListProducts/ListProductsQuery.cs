using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>
/// Server-side paged product list. Defaults: page 1, pageSize 25,
/// sorted by last update (newest first).
/// </summary>
public sealed record ListProductsQuery(
    int Page = 1,
    int PageSize = 25,
    string SortBy = ListProductsQuery.DefaultSortBy,
    string SortDir = ListProductsQuery.DefaultSortDir,
    string? Status = null,
    long? CategoryId = null,
    string? Search = null,
    /// <summary>
    /// Display locale. Null means "no preference" and resolves to the default
    /// chain in <see cref="Common.DisplayLocale"/>. An unconfigured locale is
    /// rejected rather than quietly ignored.
    /// </summary>
    string? Locale = null) : IRequest<Result<PagedResult<ProductListItemDto>>>
{
    public const string DefaultSortBy = "updated";
    public const string DefaultSortDir = "desc";
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    /// <summary>Whitelisted sort keys — the ONLY values ever turned into SQL.</summary>
    public static readonly IReadOnlyList<string> SortableColumns =
        ["code", "name", "category", "status", "quality", "owner", "updated", RelevanceSort];

    /// <summary>
    /// Orders by full-text rank. Only meaningful alongside a search term —
    /// without one the rank is 0 for every row and the "sort" would be
    /// arbitrary, so the validator rejects that combination rather than
    /// returning a result the caller would reasonably misread as ranked.
    /// </summary>
    public const string RelevanceSort = "relevance";
}
