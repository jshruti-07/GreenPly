using System.Globalization;
using Pim.Api.Endpoints;
using Pim.Api.Middleware;
using Pim.Catalog.Infrastructure;
using Pim.Catalog.Infrastructure.Outbox;
using Serilog;

Log.Logger = new LoggerConfiguration()
    // Invariant culture: log output must not vary with machine locale (CA1305).
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "pim-api"));

    builder.Services.AddCatalogModule(builder.Configuration);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks()
        .AddNpgSql(
            builder.Configuration.GetConnectionString("PimDatabase")
                ?? throw new InvalidOperationException("Connection string 'PimDatabase' is not configured."),
            name: "postgres")
        // Reports queue depth, dead letters, and whether anything is actually
        // leaving the service (ADR-007). Tagged so a readiness probe can select
        // "ready" only: the outbox degrading must not pull the API out of the
        // load balancer, because a backlog is a throughput fact, not an outage.
        .AddCheck<OutboxHealthCheck>("outbox", tags: ["messaging"]);

    var app = builder.Build();

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapHealthChecks("/healthz");
    app.MapProductEndpoints();
    app.MapLookupEndpoints();
    app.MapDashboardEndpoints();
    app.MapCategoryEndpoints();
    app.MapAttributeLibraryEndpoints();
    app.MapTranslationEndpoints();
    app.MapAssetLibraryEndpoints();
    app.MapAssetIngestEndpoints();
    app.MapWorkflowEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "pim-api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
