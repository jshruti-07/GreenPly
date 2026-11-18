using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The write protocol every product-scoped store follows:
///
///   open → begin → SELECT ... FOR UPDATE → compare rowVersion → mutate →
///   bump rowVersion → commit → re-read → return the refreshed payload
///
/// This was copied into each store as it was written. By the fifth store the
/// duplication was the real risk: the row lock, the concurrency comparison and
/// the version bump are the parts that must never be subtly different between
/// tabs, and a copied block is exactly where "subtly different" hides.
///
/// Every write goes through here. A store that forgets to bump the row version
/// would let a stale client overwrite a later change without a 409.
/// </summary>
internal static class ProductWriteScope
{
    /// <summary>Connection, transaction and resolved product id for one write.</summary>
    internal sealed record Scope(NpgsqlConnection Connection, NpgsqlTransaction Transaction, long ProductId);

    /// <summary>
    /// Runs <paramref name="mutate"/> against the locked product row, then
    /// returns the payload produced by <paramref name="refresh"/>.
    ///
    /// <paramref name="mutate"/> returns an <see cref="Error"/> to roll back, or
    /// null to commit — so a domain rejection and a successful write take the
    /// same path out and neither can forget to close the transaction.
    /// </summary>
    public static async Task<Result<T>> ExecuteAsync<T>(
        CatalogDbContext dbContext,
        string productCode,
        int expectedRowVersion,
        Func<Scope, Task<Error?>> mutate,
        Func<NpgsqlConnection, Task<T?>> refresh,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(refresh);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var (productId, rowVersion) = await LoadProductAsync(
                connection, transaction, productCode, cancellationToken);

            if (productId is null)
            {
                return Result.Failure<T>(ProductErrors.NotFound(productCode));
            }

            if (rowVersion != expectedRowVersion)
            {
                return Result.Failure<T>(ProductErrors.ConcurrentModification);
            }

            var error = await mutate(new Scope(connection, transaction, productId.Value));
            if (error is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<T>(error);
            }

            await BumpRowVersionAsync(connection, transaction, productId.Value, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var refreshed = await refresh(connection);
            return refreshed is null
                ? Result.Failure<T>(ProductErrors.NotFound(productCode))
                : Result.Success(refreshed);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Opens a connection for a read, closing it only if this call opened it.
    /// Reads share this so a store never leaks a connection it borrowed.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(
        CatalogDbContext dbContext,
        Func<NpgsqlConnection, Task<T?>> read,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(read);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await read(connection);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Resolves a product code to its id and current version, taking a row lock
    /// so two concurrent writers serialise rather than both passing the version
    /// check and one silently losing.
    /// </summary>
    public static async Task<(long? ProductId, int RowVersion)> LoadProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, row_version FROM catalog.product WHERE product_code = @code FOR UPDATE
            """;

        var row = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductVersionRow>(
            connection, sql, new { code = productCode }, cancellationToken, transaction);

        return row is null ? (null, 0) : (row.Id, row.RowVersion);
    }

    /// <summary>Resolves a product code to its id and version without locking.</summary>
    public static async Task<(long? ProductId, int RowVersion)> ResolveProductAsync(
        NpgsqlConnection connection,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, row_version FROM catalog.product WHERE product_code = @code
            """;

        var row = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductVersionRow>(
            connection, sql, new { code = productCode }, cancellationToken);

        return row is null ? (null, 0) : (row.Id, row.RowVersion);
    }

    /// <summary>Identity and concurrency token for one product.</summary>
    private sealed record ProductVersionRow(long Id, int RowVersion);

    /// <summary>
    /// The database stores workflow states lowercase ('approval'); the API
    /// exposes the domain enum name ('Approval'). Shared here because two
    /// readers projecting the same column disagreed on the casing, which showed
    /// up as a lowercase status chip on the Relationships tab (found in the
    /// browser check, 2026-08-16).
    /// </summary>
    public static string NormalizeWorkflow(string value) =>
        value.Length == 0
            ? value
            : string.Concat(
                value[..1].ToUpper(CultureInfo.InvariantCulture),
                value[1..].ToLower(CultureInfo.InvariantCulture));

    private static async Task BumpRowVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE catalog.product
            SET row_version = row_version + 1, updated_at = now()
            WHERE id = @productId
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
