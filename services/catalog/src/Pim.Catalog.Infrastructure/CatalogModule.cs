using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pim.Catalog.Application;
using Pim.Catalog.Application.Behaviors;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.AssetLibrary;
using Pim.Catalog.Infrastructure.Assets;
using Pim.Catalog.Infrastructure.Storage;
using Pim.Catalog.Application.AttributeLibrary;
using Pim.Catalog.Application.Categories;
using Pim.Catalog.Application.Translations;
using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Application.Dashboard;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Application.Attributes;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Application.Products.Classifications;
using Pim.Catalog.Application.Products.Compliance;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Products.Assets;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Application.Products.Localization;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Application.Products.Planning;
using Pim.Catalog.Infrastructure.Outbox;
using Pim.Catalog.Infrastructure.Workflow;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.Catalog.Infrastructure.ReadModels;
using Pim.Catalog.Infrastructure.Repositories;
using Pim.SharedKernel.Domain;

namespace Pim.Catalog.Infrastructure;

/// <summary>
/// Composition root for the Catalog module. The API host calls this once;
/// nothing inside the module is registered anywhere else.
/// </summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PimDatabase")
            ?? throw new InvalidOperationException("Connection string 'PimDatabase' is not configured.");

        // Dapper's mapping configuration lives in DapperGateway's static
        // constructor, not here: the integration tests construct stores
        // directly and would otherwise run unconfigured.

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

        // Shared by anything that needs a connection outside a request scope:
        // the outbox relay (a singleton background worker) and the locale
        // catalog (a process-wide cache). TryAdd, so registering it here and in
        // AddCatalogOutbox cannot produce two pools.
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        // Singleton: consulted on every read to validate the display locale,
        // over a three-row table that changes when a new market opens.
        services.TryAddSingleton<ILocaleCatalog, LocaleCatalog>();

        // Stateless wrapper over the catalog above, so it inherits that
        // lifetime. It exists because the reject-an-unconfigured-locale rule
        // must have exactly one implementation (ADR-009), which means the two
        // attribute handlers take it as a dependency — and an unregistered
        // dependency of a MediatR handler is invisible to every unit test,
        // because those construct the handler directly. It surfaced only when
        // the API was actually started: ValidateOnBuild refused the container
        // and nothing served at all (Sprint 17).
        services.TryAddSingleton<DisplayLocaleResolver>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductListReader, ProductListReader>();
        services.AddScoped<IDashboardStore, DashboardStore>();
        services.AddScoped<ICategoryStore, CategoryStore>();
        services.AddScoped<IAttributeLibraryStore, AttributeLibraryStore>();
        services.AddScoped<ITranslationWorkbenchStore, TranslationWorkbenchStore>();
        services.AddScoped<IAssetLibraryStore, AssetLibraryStore>();
        services.AddScoped<IAssetIngestStore, AssetIngestStore>();

        // Asset binaries. Singletons: both are stateless, and the disk store
        // creates its root directory once at construction rather than on every
        // request. Swapping in S3 or Azure Blob replaces this one line.
        services.Configure<LocalDiskStorageOptions>(
            configuration.GetSection(LocalDiskStorageOptions.SectionName));
        services.AddSingleton<IAssetStorage, LocalDiskAssetStorage>();
        services.AddSingleton<IRenditionGenerator, SkiaRenditionGenerator>();
        services.AddScoped<ILookupReader, LookupReader>();
        services.AddScoped<IProductAttributeStore, ProductAttributeStore>();
        services.AddScoped<IProductClassificationStore, ProductClassificationStore>();
        services.AddScoped<IProductComplianceStore, ProductComplianceStore>();
        services.AddScoped<IProductPackagingStore, ProductPackagingStore>();
        services.AddScoped<IProductRelationshipStore, ProductRelationshipStore>();
        services.AddScoped<IProductApplicationStore, ProductApplicationStore>();
        services.AddScoped<IProductAssetStore, ProductAssetStore>();
        services.AddScoped<IProductPlanningStore, ProductPlanningStore>();
        services.AddScoped<IProductLocalizationStore, ProductLocalizationStore>();
        // Workflow (Sprint 27). The definition is cached process-wide like the
        // locale catalog; everything that touches a task is scoped, because the
        // task shares its unit of work with the product it belongs to.
        services.TryAddSingleton<IWorkflowCatalog, WorkflowCatalog>();
        services.AddScoped<IApprovalTaskRepository, ApprovalTaskRepository>();
        services.AddScoped<IWorkflowReadStore, WorkflowReadStore>();
        services.AddScoped<IWorkflowBoardStore, WorkflowBoardStore>();
        services.AddScoped<WorkflowTransitionService>();
        services.AddScoped<WorkflowPresenter>();

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IProductActivityReader, ProductActivityReader>();

        // Compliance derives expiry from "today"; injecting the clock keeps the
        // boundary cases testable instead of depending on the wall clock.
        services.TryAddSingleton(TimeProvider.System);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(CatalogApplicationAssembly.Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));

            // Audit runs INSIDE validation: a command rejected by a validator
            // never reached the domain and changed nothing, so it must not
            // appear in the product's history.
            cfg.AddOpenBehavior(typeof(AuditBehavior<,>));
        });

        services.AddValidatorsFromAssembly(CatalogApplicationAssembly.Assembly, includeInternalTypes: true);

        // Transactional outbox (ADR-007). The enqueue side needs no
        // registration — it is static, inside SaveChangesAsync — so what this
        // adds is the relay that drains what the enqueue wrote.
        services.AddCatalogOutbox(configuration, connectionString);

        return services;
    }
}
