using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The one place a read model gets a connection and runs a query.
///
/// Every store borrows the connection EF Core already owns, so a read inside a
/// write transaction sees that transaction's uncommitted rows — which is what
/// makes "mutate, then re-read and return the refreshed payload" work in
/// <see cref="ProductWriteScope"/>. Opening a second connection would read the
/// pre-write state and return a payload that contradicts the write that just
/// succeeded.
///
/// The open/close dance is here rather than repeated per store because
/// "close only if this call opened it" is exactly the kind of bookkeeping that
/// is wrong in the fifth copy.
/// </summary>
internal static class DapperGateway
{
    /// <summary>
    /// Dapper's settings are process-wide mutable state, and they are applied
    /// here rather than in the composition root because they are a property of
    /// USING Dapper, not of registering services.
    ///
    /// That distinction was not theoretical. These lived in AddCatalogModule
    /// first, and the integration tests construct their stores directly — so
    /// every projection failed to materialise with "a parameterless default
    /// constructor is required", because snake_case mapping had never been
    /// switched on. Any future caller that news up a store, a background worker
    /// or a console tool, would have hit the same wall.
    ///
    /// A static constructor runs exactly once, on first use of this type, and
    /// every read in the service goes through this type.
    /// </summary>
    static DapperGateway()
    {
        // catalog columns are snake_case; the DTOs are PascalCase.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // timestamptz -> DateTimeOffset. Npgsql hands Dapper a DateTime and
        // several DTOs declare DateTimeOffset, which is the API contract.
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }

    public static Task<IReadOnlyList<T>> QueryAsync<T>(
        CatalogDbContext dbContext,
        string sql,
        object? parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            dbContext,
            async connection =>
            {
                var rows = await connection.QueryAsync<T>(
                    new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
                return (IReadOnlyList<T>)rows.AsList();
            },
            cancellationToken);

    public static Task<T?> QuerySingleOrDefaultAsync<T>(
        CatalogDbContext dbContext,
        string sql,
        object? parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            dbContext,
            connection => connection.QuerySingleOrDefaultAsync<T>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);

    /// <summary>Queries on a connection the caller already holds open.</summary>
    public static async Task<IReadOnlyList<T>> QueryOnAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object? parameters,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var rows = await connection.QueryAsync<T>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>Single row on a connection the caller already holds open.</summary>
    public static Task<T?> QuerySingleOrDefaultOnAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object? parameters,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null) =>
        connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

    /// <summary>Non-query on a connection the caller already holds open.</summary>
    public static Task<int> ExecuteOnAsync(
        NpgsqlConnection connection,
        string sql,
        object? parameters,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null) =>
        connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

    private static async Task<TResult> ExecuteAsync<TResult>(
        CatalogDbContext dbContext,
        Func<NpgsqlConnection, Task<TResult>> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await query(connection);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }
}
