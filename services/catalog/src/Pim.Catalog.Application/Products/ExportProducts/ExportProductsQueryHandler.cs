using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ExportProducts;

/// <summary>
/// Renders the current grid selection (or filter set) as CSV. Reuses the grid's
/// read model so the export can never drift from what the user sees.
///
/// That guarantee now includes language: the export takes the same display
/// locale the grid was rendered in. A CSV in a different language from the
/// screen it was exported from would be the exact drift this handler exists to
/// prevent, and the harder kind to notice — it only shows up once the file is
/// somewhere else.
/// </summary>
public sealed class ExportProductsQueryHandler(IProductListReader reader, ILocaleCatalog locales)
    : IRequestHandler<ExportProductsQuery, Result<string>>
{
    public async Task<Result<string>> Handle(
        ExportProductsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configured = await locales.GetAllAsync(cancellationToken);
        var displayLocale = DisplayLocale.Resolve(request.Locale, configured);

        if (displayLocale is null)
        {
            return Result.Failure<string>(LocaleErrors.Unknown(request.Locale!));
        }

        WorkflowState? workflowState = null;
        if (request.Status is not null)
        {
            workflowState = Enum.Parse<WorkflowState>(request.Status, ignoreCase: true);
        }

        var codes = request.Codes is { Count: > 0 }
            ? request.Codes.Select(c => c.Trim()).Where(c => c.Length > 0).ToList()
            : null;

        var filter = new ProductListFilter(
            Page: 1,
            PageSize: ExportProductsQuery.MaxRows,
            SortBy: request.SortBy.ToLowerInvariant(),
            SortDescending: request.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase),
            WorkflowState: workflowState,
            CategoryId: request.CategoryId,
            Search: string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            DisplayLocale: displayLocale,
            Codes: codes);

        var page = await reader.ListAsync(filter, cancellationToken);

        return ProductCsv.Render(page.Items);
    }
}
