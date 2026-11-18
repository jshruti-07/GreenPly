using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Registers the outbox relay and everything it needs.
///
/// Kept out of <c>AddCatalogModule</c>'s body only for readability; it is still
/// called from there, so there remains exactly one composition root for this
/// service. Note what is NOT here: <see cref="IntegrationEventMapper"/> is
/// static and needs no registration, which is what lets the integration fixture
/// construct a DbContext directly and still get outbox rows (R-12).
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>());

        // The relay is a singleton background worker with no request scope, so
        // it cannot borrow EF's scoped connection the way the read models do.
        // A data source owns its own pool; one instance for the process.
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.TryAddSingleton<OutboxStore>();
        services.TryAddSingleton<OutboxMetrics>();

        // The broker, when one is configured. Registered BEFORE the log-only
        // transport so the TryAdd below is a no-op -- which is what the comment
        // on that line has always promised.
        //
        // Presence of a Uri is the switch, and nothing else. No enabled flag: a
        // second way to say the same thing is a second way to get it wrong, and
        // "the broker is configured but messaging is off" is a state nobody
        // needs and everybody would eventually hit by accident.
        var rabbit = configuration.GetSection(RabbitMqOptions.SectionName);
        services.Configure<RabbitMqOptions>(rabbit);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>());

        if (!string.IsNullOrWhiteSpace(rabbit["Uri"]))
        {
            services.TryAddSingleton<IOutboxTransport, RabbitMqOutboxTransport>();
        }

        // TryAdd, so a host that has registered a real transport keeps it.
        // Reached only when no broker is configured -- see ADR-007 §Deferred,
        // now closed.
        services.TryAddSingleton<IOutboxTransport, LoggingOutboxTransport>();

        services.TryAddSingleton<OutboxHealthCheck>();

        services.AddHostedService<OutboxRelay>();

        return services;
    }
}
