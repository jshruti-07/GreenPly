using FluentValidation;
using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>Input-shape validation only; business rules live in Domain.</summary>
public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, ListProductsQuery.MaxPageSize);

        RuleFor(q => q.SortBy)
            .Must(s => ListProductsQuery.SortableColumns.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(q =>
                $"sortBy '{q.SortBy}' is not sortable. Allowed: {string.Join(", ", ListProductsQuery.SortableColumns)}.");

        RuleFor(q => q.SortDir)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be 'asc' or 'desc'.");

        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.TryParse<WorkflowState>(s, ignoreCase: true, out _))
            .WithMessage(q =>
                $"status '{q.Status}' is unknown. Allowed: {string.Join(", ", Enum.GetNames<WorkflowState>())}.");

        RuleFor(q => q.CategoryId)
            .GreaterThanOrEqualTo(1).When(q => q.CategoryId.HasValue);

        RuleFor(q => q.Search)
            .MaximumLength(ListProductsQuery.MaxSearchLength);

        // Relevance without something to be relevant to is not a sort order.
        RuleFor(q => q.SortBy)
            .Must((query, _) => !string.IsNullOrWhiteSpace(query.Search))
            .When(q => string.Equals(q.SortBy, ListProductsQuery.RelevanceSort,
                                     StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortBy 'relevance' requires a search term.");
    }
}
