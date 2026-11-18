using FluentValidation;

namespace Pim.Catalog.Application.Lookups;

public sealed class GetTaxonomyNodesQueryValidator : AbstractValidator<GetTaxonomyNodesQuery>
{
    /// <summary>Matches the taxonomy_node.node_type CHECK constraint.</summary>
    public static readonly IReadOnlyList<string> NodeTypes =
        ["BRAND", "FAMILY", "CATEGORY", "SUBCATEGORY", "SERIES"];

    public GetTaxonomyNodesQueryValidator()
    {
        RuleFor(q => q.NodeType)
            .Must(t => NodeTypes.Contains(t!, StringComparer.OrdinalIgnoreCase))
            .When(q => !string.IsNullOrWhiteSpace(q.NodeType))
            .WithMessage(q => $"nodeType '{q.NodeType}' is unknown. Allowed: {string.Join(", ", NodeTypes)}.");
    }
}
