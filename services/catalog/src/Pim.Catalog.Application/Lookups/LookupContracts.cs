using MediatR;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Lookups;

/// <summary>Brand option for the create wizard's Brand select.</summary>
public sealed record BrandLookupDto(long Id, string Code, string Name);

/// <summary>
/// Taxonomy node option. <paramref name="NodeType"/> lets the wizard split
/// Product Family from Category, and <paramref name="ParentId"/> allows the
/// category list to be filtered by the chosen family.
/// </summary>
public sealed record TaxonomyNodeLookupDto(
    long Id,
    string Code,
    string Name,
    string NodeType,
    long? ParentId,
    long? AttributeSetId);

public sealed record GetBrandsQuery : IRequest<Result<IReadOnlyList<BrandLookupDto>>>;

/// <summary>Optional node-type filter, e.g. 'CATEGORY'.</summary>
public sealed record GetTaxonomyNodesQuery(string? NodeType = null)
    : IRequest<Result<IReadOnlyList<TaxonomyNodeLookupDto>>>;

/// <summary>Read-side port for wizard lookups; SQL lives in Infrastructure.</summary>
public interface ILookupReader
{
    Task<IReadOnlyList<BrandLookupDto>> GetBrandsAsync(string locale, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxonomyNodeLookupDto>> GetTaxonomyNodesAsync(
        string locale,
        string? nodeType,
        CancellationToken cancellationToken);
}
