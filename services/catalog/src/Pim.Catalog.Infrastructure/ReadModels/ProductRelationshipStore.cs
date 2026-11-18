using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Domain.Packaging;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Typed links from this product to others.
///
/// Links are stored one-directional. An accessory of a door is not necessarily
/// an accessory of that accessory, so writing an automatic inverse row would
/// invent a claim nobody made. Where a relationship genuinely is symmetric
/// (COMPATIBLE), the steward records both directions deliberately.
/// </summary>
public sealed class ProductRelationshipStore(CatalogDbContext dbContext) : IProductRelationshipStore
{
    public Task<ProductRelationshipsDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, productCode, locale, cancellationToken),
            cancellationToken);

    public Task<Result<ProductRelationshipsDto>> AddAsync(
        AddRelationshipCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var targetId = await ResolveProductIdAsync(
                    scope.Connection, scope.Transaction, command.TargetCode, cancellationToken);

                if (targetId is null)
                {
                    return RelationshipErrors.TargetNotFound(command.TargetCode);
                }

                // Also enforced by ck_rel_not_self; checked here for a clear message.
                if (targetId.Value == scope.ProductId)
                {
                    return RelationshipErrors.SelfReference;
                }

                const string sql = """
                    INSERT INTO catalog.product_relationship
                        (from_product_id, to_product_id, relation_type, priority)
                    VALUES (@from, @to, @type, @priority)
                    ON CONFLICT (from_product_id, to_product_id, relation_type) DO NOTHING
                    """;

                await using var insert = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
                insert.Parameters.AddWithValue("from", scope.ProductId);
                insert.Parameters.AddWithValue("to", targetId.Value);
                insert.Parameters.AddWithValue("type", command.RelationType);
                insert.Parameters.AddWithValue("priority", command.Priority);

                // DO NOTHING returns 0 when the link already exists. Reporting
                // that as a conflict is more honest than a silent success that
                // leaves the existing priority untouched.
                var affected = await insert.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0
                    ? RelationshipErrors.Duplicate(command.TargetCode, command.RelationType)
                    : null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductRelationshipsDto>> RemoveAsync(
        RemoveRelationshipCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                const string sql = """
                    DELETE FROM catalog.product_relationship r
                    USING catalog.product t
                    WHERE r.to_product_id = t.id
                      AND r.from_product_id = @from
                      AND t.product_code = @targetCode
                      AND r.relation_type = @type
                    """;

                await using var delete = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
                delete.Parameters.AddWithValue("from", scope.ProductId);
                delete.Parameters.AddWithValue("targetCode", command.TargetCode);
                delete.Parameters.AddWithValue("type", command.RelationType);

                var affected = await delete.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0
                    ? RelationshipErrors.NotFound(command.TargetCode, command.RelationType)
                    : null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    private static async Task<long?> ResolveProductIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT id FROM catalog.product WHERE product_code = @code";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("code", productCode);

        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<ProductRelationshipsDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        var (productId, rowVersion) = await ProductWriteScope.ResolveProductAsync(
            connection, productCode, cancellationToken);

        if (productId is null)
        {
            return null;
        }

        // The target's name comes along so a card can render without a
        // follow-up request per link, and it follows the reader's language for
        // the same reason the grid's does: a link from an English screen that
        // names its target in German is the inconsistency R-15 was about.
        //
        // Fallback is to the TARGET's base locale, not this product's. Two
        // products in one catalogue can carry different masters, and resolving
        // a target's name against the source's base would silently show the
        // wrong language for exactly the links that cross that boundary.
        const string sql = """
            SELECT r.relation_type,
                   t.product_code                     AS target_code,
                   COALESCE(tr_d.name, tr_b.name)     AS target_name,
                   t.workflow_state                   AS target_workflow_state,
                   r.priority
            FROM catalog.product_relationship r
            JOIN catalog.product t ON t.id = r.to_product_id
            LEFT JOIN catalog.product_tr tr_d
                   ON tr_d.product_id = t.id AND tr_d.locale_code = @displayLocale
            LEFT JOIN catalog.product_tr tr_b
                   ON tr_b.product_id = t.id AND tr_b.locale_code = t.base_locale
            WHERE r.from_product_id = @productId
            ORDER BY r.relation_type, r.priority, t.product_code
            """;

        var rows = await DapperGateway.QueryOnAsync<RelationshipRow>(
            connection,
            sql,
            new { productId = productId.Value, displayLocale = locale.Code },
            cancellationToken);

        var byType = new Dictionary<string, List<ProductRelationshipDto>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!byType.TryGetValue(row.RelationType, out var items))
            {
                items = [];
                byType[row.RelationType] = items;
            }

            items.Add(new ProductRelationshipDto(
                row.RelationType,
                row.TargetCode,
                row.TargetName,
                // The DB stores workflow states lowercase; the API exposes the
                // enum name. Shared so two readers cannot disagree.
                ProductWriteScope.NormalizeWorkflow(row.TargetWorkflowState),
                row.Priority));
        }

        // Every type is returned, empty ones included, so the tab shows all the
        // link kinds a steward can populate rather than only what exists.
        var groups = RelationType.All
            .Select(type => new RelationshipGroupDto(
                type,
                byType.TryGetValue(type, out var items) ? items : []))
            .ToList();

        return new ProductRelationshipsDto(productCode, rowVersion, groups);
    }

    private sealed record RelationshipRow(
        string RelationType,
        string TargetCode,
        string? TargetName,
        string TargetWorkflowState,
        int Priority);
}
