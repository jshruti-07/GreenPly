using Microsoft.EntityFrameworkCore;
using NSubstitute;
using MediatR;
using Pim.Catalog.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pim.Catalog.Integration.Tests;

/// <summary>
/// One PostgreSQL container per test collection, initialized with the REAL
/// catalog DDL (00_roles + 01_catalog_schema) so schema drift fails loudly.
/// Requires a running Docker daemon (dev Mac or CI agent).
/// </summary>
public sealed class PostgresCatalogFixture : IAsyncLifetime
{
    // Image passed to the constructor: the parameterless overload is obsolete
    // in Testcontainers 4.13. Pinned to the same image infra/docker-compose uses.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pim")
        .WithUsername("pim_admin")
        .WithPassword("pim_admin_local_dev")
        .Build();

    public string ConnectionString =>
        $"{_container.GetConnectionString()};Search Path=catalog";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        foreach (var script in new[] { "db/00_roles.sql", "db/01_catalog_schema.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, script));
            var result = await _container.ExecScriptAsync(sql);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Applying {script} failed: {result.Stderr}");
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Context for test arrange/act; publisher is a no-op substitute.</summary>
    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new CatalogDbContext(options, Substitute.For<IPublisher>());
    }

    /// <summary>Runs raw SQL for fixture seeding.</summary>
    public async Task ExecuteAsync(string sql)
    {
        var result = await _container.ExecScriptAsync(sql);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Fixture SQL failed: {result.Stderr}");
        }
    }
}

/// <summary>
/// xUnit collection definition sharing one container across test classes.
/// Named ...Definition rather than ...Collection to satisfy CA1711 (the
/// 'Collection' suffix is reserved for types implementing ICollection).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresFixtureDefinition : ICollectionFixture<PostgresCatalogFixture>
{
    public const string Name = "postgres";
}
