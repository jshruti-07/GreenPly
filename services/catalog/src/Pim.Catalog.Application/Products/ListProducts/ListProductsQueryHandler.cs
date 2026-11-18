using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>
/// Thin orchestration: inputs are already shape-validated by the pipeline;
/// this normalizes them into a <see cref="ProductListFilter"/> and delegates
/// to the read model.
///
/// One expected business failure now exists: an unconfigured display locale.
/// It is checked here rather than in the validator because "this catalogue has
/// a French locale" is a data question, not an input-shape one, and the
/// project's rule is that FluentValidation covers shape only.
/// </summary>
public sealed class ListProductsQueryHandler(IProductListReader reader, ILocaleCatalog locales)
    : IRequestHandler<ListProductsQuery, Result<PagedResult<ProductListItemDto>>>
{
    public async Task<Result<PagedResult<ProductListItemDto>>> Handle(
        ListProductsQuery request,
        CancellationToken cancellationToken)
    {
        var configured = await locales.GetAllAsync(cancellationToken);
        var displayLocale = DisplayLocale.Resolve(request.Locale, configured);

        if (displayLocale is null)
        {
            return Result.Failure<PagedResult<ProductListItemDto>>(
                LocaleErrors.Unknown(request.Locale!));
        }

        WorkflowState? workflowState = null;
        if (request.Status is not null)
        {
            workflowState = Enum.Parse<WorkflowState>(request.Status, ignoreCase: true);
        }

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        var filter = new ProductListFilter(
            request.Page,
            request.PageSize,
            request.SortBy.ToLowerInvariant(),
            SortDescending: request.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase),
            workflowState,
            request.CategoryId,
            search,
            displayLocale);

        return await reader.ListAsync(filter, cancellationToken);
    }
}
