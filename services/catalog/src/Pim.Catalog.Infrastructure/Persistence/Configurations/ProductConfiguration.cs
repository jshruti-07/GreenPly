using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pim.Catalog.Domain.Products;

namespace Pim.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Product"/> to the approved `product` table
/// (pim_database_v2.0.sql). Columns not yet modelled in the domain
/// (successor/predecessor, attributes_cache, search_vector, owner) are added
/// in later increments; the SQL schema remains the source of truth.
/// </summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product", "catalog");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(p => p.PublicId).HasColumnName("public_id").IsRequired();
        builder.HasIndex(p => p.PublicId).IsUnique();

        builder.Property(p => p.Code)
            .HasColumnName("product_code")
            .HasMaxLength(ProductCode.MaxLength)
            .IsRequired()
            .HasConversion(
                code => code.Value,
                value => ProductCode.Create(value).Value);
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.PrimaryNodeId).HasColumnName("primary_node_id").IsRequired();
        builder.Property(p => p.BrandId).HasColumnName("brand_id");
        builder.Property(p => p.AttributeSetId).HasColumnName("attribute_set_id");
        builder.Property(p => p.BaseLocale).HasColumnName("base_locale").IsRequired();

        builder.Property(p => p.LifecycleState)
            .HasColumnName("lifecycle_state")
            .IsRequired()
            .HasConversion(
                state => ToDbLifecycle(state),
                value => FromDbLifecycle(value));

        builder.Property(p => p.WorkflowState)
            .HasColumnName("workflow_state")
            .IsRequired()
            .HasConversion(
                state => state.ToString().ToLowerInvariant(),
                value => Enum.Parse<WorkflowState>(value, true));

        builder.Property(p => p.Gtin).HasColumnName("gtin");
        builder.Property(p => p.SapMaterialNo).HasColumnName("sap_material_no");
        builder.Property(p => p.OwnerDisplayName).HasColumnName("owner_display_name");

        // product_tr is part of this aggregate: loaded and saved with the root,
        // never queried independently (the grid reads it via the read model).
        builder.OwnsMany(p => p.Translations, translation =>
        {
            translation.ToTable("product_tr", "catalog");
            translation.WithOwner().HasForeignKey(t => t.ProductId);
            translation.HasKey(t => new { t.ProductId, t.LocaleCode });

            translation.Property(t => t.ProductId).HasColumnName("product_id");
            translation.Property(t => t.LocaleCode).HasColumnName("locale_code").IsRequired();
            translation.Property(t => t.Name)
                .HasColumnName("name")
                .HasMaxLength(ProductTranslation.MaxNameLength)
                .IsRequired();
            translation.Property(t => t.TranslationStatus).HasColumnName("translation_status").IsRequired();
            translation.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at").IsRequired();
        });

        // SKUs are part of the aggregate: loaded and saved with the root.
        builder.OwnsMany(p => p.Skus, sku =>
        {
            sku.ToTable("sku", "catalog");
            sku.WithOwner().HasForeignKey(s => s.ProductId);
            sku.HasKey(s => s.Id);

            sku.Property(s => s.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            sku.Property(s => s.ProductId).HasColumnName("product_id");
            sku.Property(s => s.Code)
                .HasColumnName("sku_code")
                .HasMaxLength(Sku.MaxCodeLength)
                .IsRequired();
            sku.HasIndex(s => s.Code).IsUnique();
            sku.Property(s => s.Gtin).HasColumnName("gtin");
            sku.Property(s => s.ErpMaterialNo).HasColumnName("erp_material_no");
            sku.Property(s => s.SortOrder).HasColumnName("sort_order");
            sku.Property(s => s.Status)
                .HasColumnName("status")
                .IsRequired()
                .HasConversion(status => SkuStatusMap.ToDb(status), value => SkuStatusMap.FromDb(value));
        });

        builder.Ignore(p => p.BaseLocaleName);
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at").IsRequired();

        builder.Property(p => p.RowVersion)
            .HasColumnName("row_version")
            .IsConcurrencyToken();

        builder.Ignore(p => p.DomainEvents);
    }

    private static string ToDbLifecycle(LifecycleState state) => state switch
    {
        LifecycleState.Planned => "PLANNED",
        LifecycleState.Active => "ACTIVE",
        LifecycleState.PhaseOut => "PHASE_OUT",
        LifecycleState.Discontinued => "DISCONTINUED",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped lifecycle state.")
    };

    private static LifecycleState FromDbLifecycle(string value) => value switch
    {
        "PLANNED" => LifecycleState.Planned,
        "ACTIVE" => LifecycleState.Active,
        "PHASE_OUT" => LifecycleState.PhaseOut,
        "DISCONTINUED" => LifecycleState.Discontinued,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unmapped lifecycle_state value.")
    };
}
