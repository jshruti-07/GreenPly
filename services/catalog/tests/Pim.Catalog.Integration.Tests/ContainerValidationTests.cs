using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pim.Catalog.Infrastructure;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests;

/// <summary>
/// Builds the service container the way the API does, and fails if anything in
/// it cannot be constructed.
///
/// WHY THIS EXISTS
/// ---------------
/// Sprint 17 shipped a handler that took a dependency nobody had registered.
/// The build was clean. All 333 unit tests passed. The API then refused to
/// start at all — <c>ValidateOnBuild</c> rejected the container — and the
/// failure was found by the smoke suite, minutes into a run that needs Docker,
/// a database and a seeded catalogue.
///
/// Unit tests cannot see this class of defect, and it is not their fault: they
/// construct handlers directly with <c>new</c>, which is the right way to test a
/// handler and precisely why the missing registration is invisible to them. The
/// composition root is a separate thing and needs its own test.
///
/// This one costs a few milliseconds and moves that failure from the smoke run
/// into the gate. It needs no database: <c>ValidateOnBuild</c> checks that every
/// registration's constructor arguments are resolvable, and never invokes the
/// factory delegates, so the connection string below is deliberately one that
/// could not connect if anything tried.
/// </summary>
public sealed class ContainerValidationTests
{
    /// <summary>
    /// Unroutable on purpose. If a future registration starts opening a
    /// connection during construction rather than on first use, this test
    /// should fail loudly rather than quietly acquire a real database
    /// dependency that only shows up as a slow, flaky suite later.
    /// </summary>
    private const string UnusableConnectionString =
        "Host=127.0.0.1;Port=1;Database=validation_only;Username=none;Password=none";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PimDatabase"] = UnusableConnectionString,
            })
            .Build();

    [Fact]
    public void EveryRegisteredServiceCanBeConstructed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogModule(Configuration());

        // ValidateOnBuild walks every descriptor and resolves its call site.
        // ValidateScopes is the second half of the same idea: it rejects a
        // singleton that captures a scoped service, which is the bug that does
        // not fail on startup at all — it fails later, under load, when a
        // captured DbContext is used from two requests at once.
        var options = new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        };

        var build = Should.NotThrow(() => services.BuildServiceProvider(options));
        build.ShouldNotBeNull();
        build.Dispose();
    }

    /// <summary>
    /// The specific shape of the Sprint 17 defect, pinned separately from the
    /// blanket check above so a failure names what actually broke.
    ///
    /// MediatR resolves handlers out of the container at dispatch time, so a
    /// handler whose dependency is unregistered compiles, registers, and throws
    /// on the first request that reaches it. Every one of them is resolved here.
    /// </summary>
    [Fact]
    public void EveryMediatRHandlerResolves()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogModule(Configuration());

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var handlerTypes = services
            .Where(d => d.ServiceType.IsGenericType
                     && d.ServiceType.GetGenericTypeDefinition().Name.StartsWith(
                            "IRequestHandler", StringComparison.Ordinal))
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();

        // If this ever reads zero the test has stopped testing anything —
        // a rename in MediatR would do it — so the count is asserted, not
        // assumed. R-10: a check that cannot fail is counted as evidence
        // while providing none.
        handlerTypes.Count.ShouldBeGreaterThan(20);

        var unresolvable = new List<string>();
        foreach (var handlerType in handlerTypes)
        {
            try
            {
                scope.ServiceProvider.GetRequiredService(handlerType);
            }
            catch (InvalidOperationException ex)
            {
                unresolvable.Add($"{handlerType.Name}: {ex.Message}");
            }
        }

        unresolvable.ShouldBeEmpty();
    }
}
