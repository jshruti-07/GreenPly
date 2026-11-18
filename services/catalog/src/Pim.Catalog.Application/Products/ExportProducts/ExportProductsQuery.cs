using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ExportProducts;

/// <summary>
/// CSV export of the grid. Either the current filter set, or an explicit list
/// of product codes (the user's row selection). Bounded by
/// <see cref="MaxRows"/> so a mis-click cannot stream the entire catalogue.
/// </summary>
public sealed record ExportProductsQuery(
    string SortBy = ListProductsQuery.DefaultSortBy,
    string SortDir = ListProductsQuery.DefaultSortDir,
    string? Status = null,
    long? CategoryId = null,
    string? Search = null,
    IReadOnlyList<string>? Codes = null,
    /// <summary>
    /// Display locale for the exported text. The caller passes whatever the
    /// grid is currently showing, so the file matches the screen.
    /// </summary>
    string? Locale = null) : IRequest<Result<string>>
{
    public const int MaxRows = 5000;
    public const int MaxSelectedCodes = 500;
}

public sealed class ExportProductsQueryValidator : AbstractValidator<ExportProductsQuery>
{
    public ExportProductsQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(s => ListProductsQuery.SortableColumns.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(q => $"sortBy '{q.SortBy}' is not sortable.");

        // Same rule as the grid: the export shares the sort whitelist, so it
        // inherits 'relevance' and must inherit the constraint that goes with
        // it. Validating in one place and not the other is how an endpoint
        // ends up with a quietly different contract.
        RuleFor(q => q.SortBy)
            .Must((query, _) => !string.IsNullOrWhiteSpace(query.Search))
            .When(q => string.Equals(q.SortBy, ListProductsQuery.RelevanceSort,
                                     StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortBy 'relevance' requires a search term.");

        RuleFor(q => q.SortDir)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be 'asc' or 'desc'.");

        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.TryParse<WorkflowState>(s, ignoreCase: true, out _))
            .WithMessage(q => $"status '{q.Status}' is unknown.");

        RuleFor(q => q.Search)
            .MaximumLength(ListProductsQuery.MaxSearchLength);

        RuleFor(q => q.Codes!)
            .Must(codes => codes.Count <= ExportProductsQuery.MaxSelectedCodes)
            .When(q => q.Codes is not null)
            .WithMessage($"At most {ExportProductsQuery.MaxSelectedCodes} products can be exported by selection.");
    }
}
