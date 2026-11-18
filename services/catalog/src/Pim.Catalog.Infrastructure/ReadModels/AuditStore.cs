using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Append-only writer for audit_log.
///
/// Entries are keyed to the PRODUCT even when the change was to a SKU or a
/// certificate. The Activity tab answers "what happened to this product", and
/// keying sub-entities separately would fragment that history across rows the
/// tab would then have to stitch back together on every read.
///
/// actor_user_id is written as NULL: no identity service exists yet. It is not
/// backfilled with a placeholder user, because a fabricated actor in an audit
/// trail is worse than an absent one.
/// </summary>
public sealed class AuditWriter(CatalogDbContext dbContext) : IAuditWriter
{
    private const string EntityType = "product";

    public Task WriteAsync(
        string productCode,
        string action,
        string detail,
        string source,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            async connection =>
            {
                var (productId, _) = await ProductWriteScope.ResolveProductAsync(
                    connection, productCode, cancellationToken);

                if (productId is null)
                {
                    // The product was deleted between the change and this write.
                    // There is nothing to attach the entry to; the behaviour
                    // logs the miss rather than failing the user's request.
                    return string.Empty;
                }

                const string sql = """
                    INSERT INTO catalog.audit_log
                        (occurred_at, actor_user_id, actor_display, action,
                         entity_type, entity_id, new_value, source)
                    VALUES (now(), NULL, NULL, @action,
                            @entityType, @entityId, @newValue, @source)
                    """;

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("action", action);
                command.Parameters.AddWithValue("entityType", EntityType);
                command.Parameters.AddWithValue("entityId", productId.Value);
                command.Parameters.AddWithValue("source", source);
                command.Parameters.Add(new NpgsqlParameter("newValue", NpgsqlDbType.Jsonb)
                {
                    Value = JsonSerializer.Serialize(new { detail }),
                });

                await command.ExecuteNonQueryAsync(cancellationToken);
                return string.Empty;
            },
            cancellationToken);
}

/// <summary>
/// Reads a product's history newest-first.
///
/// This is the only paged tab in the detail screen: audit rows accumulate
/// forever, so returning the whole set — which every other tab does — would
/// degrade steadily and without warning as a product is edited.
/// </summary>
public sealed class ProductActivityReader(CatalogDbContext dbContext) : IProductActivityReader
{
    public Task<ProductActivityDto?> GetAsync(
        GetProductActivityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, query, cancellationToken),
            cancellationToken);
    }

    private static async Task<ProductActivityDto?> ReadAsync(
        NpgsqlConnection connection,
        GetProductActivityQuery query,
        CancellationToken cancellationToken)
    {
        var (productId, _) = await ProductWriteScope.ResolveProductAsync(
            connection, query.Code, cancellationToken);

        if (productId is null)
        {
            return null;
        }

        var hasAction = !string.IsNullOrWhiteSpace(query.Action);
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        // Filters are applied in SQL rather than after paging; filtering a page
        // client-side would return short pages and a total that disagrees.
        const string filter = """
            WHERE entity_type = 'product'
              AND entity_id = @productId
              AND (@action::text IS NULL OR action = @action)
              AND (@search::text IS NULL OR
                   (COALESCE(new_value->>'detail', '') || ' ' || action) ILIKE '%' || @search || '%')
            """;

        var filterArgs = BuildFilterArgs(productId.Value, query, hasAction, hasSearch);

        var total = await DapperGateway.QuerySingleOrDefaultOnAsync<int>(
            connection,
            $"SELECT count(*) FROM catalog.audit_log {filter}",
            filterArgs,
            cancellationToken);

        var entries = new List<ActivityEntryDto>();
        var pageSql = $"""
            SELECT id,
                   occurred_at              AS occurred_at,
                   actor_display            AS actor,
                   action,
                   new_value->>'detail'     AS detail,
                   source
            FROM catalog.audit_log
            {filter}
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit OFFSET @offset
            """;

        // Filters are still assembled as NpgsqlParameters because the WHERE
        // clause is built conditionally; Dapper takes them as a dynamic bag.
        var pageArgs = BuildFilterArgs(productId.Value, query, hasAction, hasSearch);
        pageArgs.Add("limit", query.PageSize);
        pageArgs.Add("offset", (query.Page - 1) * query.PageSize);

        entries.AddRange(await DapperGateway.QueryOnAsync<ActivityEntryDto>(
            connection, pageSql, pageArgs, cancellationToken));

        // The filter dropdown offers only actions this product actually has, so
        // it can never present a choice that returns nothing.
        var actions = new List<string>();
        const string actionsSql = """
            SELECT DISTINCT action FROM catalog.audit_log
            WHERE entity_type = 'product' AND entity_id = @productId
            ORDER BY action
            """;

        actions.AddRange(await DapperGateway.QueryOnAsync<string>(
            connection, actionsSql, new { productId = productId.Value }, cancellationToken));

        return new ProductActivityDto(
            query.Code,
            actions,
            new PagedResult<ActivityEntryDto>(entries, query.Page, query.PageSize, total));
    }

    /// <summary>
    /// The filter values shared by the count and the page query.
    ///
    /// A DynamicParameters bag rather than an anonymous object because the two
    /// queries take different extra parameters (the page adds limit/offset) and
    /// both must see IDENTICAL filters — a count that filters differently from
    /// the page it counts is the classic paging bug.
    ///
    /// Nulls are typed explicitly: the SQL casts with @action::text so Postgres
    /// can resolve the comparison, and an untyped null would leave it unable to.
    /// </summary>
    private static DynamicParameters BuildFilterArgs(
        long productId,
        GetProductActivityQuery query,
        bool hasAction,
        bool hasSearch)
    {
        var parameters = new DynamicParameters();
        parameters.Add("productId", productId, DbType.Int64);
        parameters.Add("action", hasAction ? query.Action! : null, DbType.String);
        parameters.Add("search", hasSearch ? query.Search!.Trim() : null, DbType.String);
        return parameters;
    }
}
