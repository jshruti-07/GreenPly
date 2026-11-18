using Dapper;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Wizard lookups. Only active rows are offered, and display names fall back
/// to the entity code when a translation is missing, so a half-translated
/// catalogue never yields blank options.
/// </summary>
public sealed class LookupReader(CatalogDbContext dbContext) : ILookupReader
{
    public async Task<IReadOnlyList<BrandLookupDto>> GetBrandsAsync(
        string locale,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.id, b.code, COALESCE(tr.name, b.code) AS name
            FROM catalog.brand b
            LEFT JOIN catalog.brand_tr tr ON tr.brand_id = b.id AND tr.locale_code = @locale
            WHERE b.status = 'active'
            ORDER BY name
            """;

        var rows = await DapperGateway.QueryAsync<BrandLookupDto>(
            dbContext, sql, new { locale }, cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<TaxonomyNodeLookupDto>> GetTaxonomyNodesAsync(
        string locale,
        string? nodeType,
        CancellationToken cancellationToken)
    {
        // Columns are aliased to the DTO's names where they differ; the rest
        // map by MatchNamesWithUnderscores (node_type -> NodeType).
        const string sql = """
            SELECT tn.id, tn.code, COALESCE(tr.name, tn.code) AS name,
                   tn.node_type, tn.parent_id, tn.attribute_set_id
            FROM catalog.taxonomy_node tn
            LEFT JOIN catalog.taxonomy_node_tr tr
                   ON tr.node_id = tn.id AND tr.locale_code = @locale
            WHERE tn.status = 'active'
              AND (@nodeType IS NULL OR tn.node_type = @nodeType)
            ORDER BY tn.depth, name
            """;

        return await DapperGateway.QueryAsync<TaxonomyNodeLookupDto>(
            dbContext, sql, new { locale, nodeType }, cancellationToken);
    }
}
