using MediatR;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Lookups;

/// <summary>
/// Lookup display names are resolved in this locale until the i18n sprint
/// (assumption A3) introduces a request-scoped locale.
/// </summary>
internal static class LookupDefaults
{
    public const string Locale = "de-DE";
}

public sealed class GetBrandsQueryHandler(ILookupReader reader)
    : IRequestHandler<GetBrandsQuery, Result<IReadOnlyList<BrandLookupDto>>>
{
    public async Task<Result<IReadOnlyList<BrandLookupDto>>> Handle(
        GetBrandsQuery request,
        CancellationToken cancellationToken) =>
        Result.Success(await reader.GetBrandsAsync(LookupDefaults.Locale, cancellationToken));
}

public sealed class GetTaxonomyNodesQueryHandler(ILookupReader reader)
    : IRequestHandler<GetTaxonomyNodesQuery, Result<IReadOnlyList<TaxonomyNodeLookupDto>>>
{
    public async Task<Result<IReadOnlyList<TaxonomyNodeLookupDto>>> Handle(
        GetTaxonomyNodesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nodeType = string.IsNullOrWhiteSpace(request.NodeType)
            ? null
            : request.NodeType.Trim().ToUpperInvariant();

        return Result.Success(await reader.GetTaxonomyNodesAsync(
            LookupDefaults.Locale, nodeType, cancellationToken));
    }
}
