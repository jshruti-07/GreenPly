using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Products;

/// <summary>
/// Product aggregate root. Maps to the approved `product` table
/// (pim_database_v2.0.sql). Taxonomy, brand and attribute-set are referenced
/// by id only in this slice; their aggregates arrive in later increments.
/// </summary>
public sealed class Product : AggregateRoot
{
    private static readonly Dictionary<LifecycleState, LifecycleState[]> AllowedTransitions = new()
    {
        [LifecycleState.Planned] = [LifecycleState.Active],
        [LifecycleState.Active] = [LifecycleState.PhaseOut],
        [LifecycleState.PhaseOut] = [LifecycleState.Discontinued],
        [LifecycleState.Discontinued] = []
    };

    private readonly List<ProductTranslation> _translations = [];
    private readonly List<Sku> _skus = [];

    private Product()
    {
        // EF Core materialization only.
        Code = null!;
        BaseLocale = null!;
    }

    public Guid PublicId { get; private set; }

    public ProductCode Code { get; private set; }

    public long PrimaryNodeId { get; private set; }

    public long? BrandId { get; private set; }

    public long? AttributeSetId { get; private set; }

    public string BaseLocale { get; private set; }

    public LifecycleState LifecycleState { get; private set; }

    public WorkflowState WorkflowState { get; private set; }

    public string? Gtin { get; private set; }

    public string? SapMaterialNo { get; private set; }

    /// <summary>
    /// Interim owner as display text (product.owner_display_name). The approved
    /// schema's owner_user_id references an identity service that does not exist
    /// yet; this column is migrated to it when auth ships (decision 2026-08-14).
    /// </summary>
    public string? OwnerDisplayName { get; private set; }

    /// <summary>Localized text, keyed by locale. Part of this aggregate.</summary>
    public IReadOnlyCollection<ProductTranslation> Translations => _translations.AsReadOnly();

    /// <summary>Saleable variants, in display order. Part of this aggregate.</summary>
    public IReadOnlyCollection<Sku> Skus => _skus.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token (product.row_version).</summary>
    public int RowVersion { get; private set; }

    /// <summary>
    /// Creates a Draft product with its base-locale name. A product without a
    /// name in its base locale is not a valid product (v4.2 create wizard);
    /// enrichment of every other field happens progressively afterwards.
    /// </summary>
    public static Result<Product> Create(
        ProductCode code,
        long primaryNodeId,
        string baseLocale,
        string name,
        long? brandId = null,
        long? attributeSetId = null,
        string? gtin = null,
        string? sapMaterialNo = null,
        string? ownerDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (primaryNodeId <= 0)
        {
            return Result.Failure<Product>(
                Error.Validation("Product.PrimaryNode.Required", "A primary taxonomy node is required."));
        }

        if (string.IsNullOrWhiteSpace(baseLocale))
        {
            return Result.Failure<Product>(
                Error.Validation("Product.BaseLocale.Required", "A base locale is required."));
        }

        var trimmedLocale = baseLocale.Trim();

        // 'human' rather than 'approved': a freshly authored name has not been
        // through review (product_tr.translation_status).
        var translationResult = ProductTranslation.Create(trimmedLocale, name, "human");
        if (translationResult.IsFailure)
        {
            return Result.Failure<Product>(translationResult.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            PublicId = Guid.NewGuid(),
            Code = code,
            PrimaryNodeId = primaryNodeId,
            BrandId = brandId,
            AttributeSetId = attributeSetId,
            BaseLocale = trimmedLocale,
            LifecycleState = LifecycleState.Active,
            WorkflowState = WorkflowState.Draft,
            Gtin = string.IsNullOrWhiteSpace(gtin) ? null : gtin.Trim(),
            SapMaterialNo = string.IsNullOrWhiteSpace(sapMaterialNo) ? null : sapMaterialNo.Trim(),
            OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? null : ownerDisplayName.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };

        product._translations.Add(translationResult.Value);

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(product.PublicId, code.Value));
        return product;
    }

    /// <summary>Name in the product's base locale; the grid's Name column.</summary>
    public string? BaseLocaleName =>
        _translations.FirstOrDefault(t => t.LocaleCode == BaseLocale)?.Name;

    /// <summary>Adds or updates the name for a locale.</summary>
    public Result SetName(string localeCode, string name)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return Result.Failure(ProductTranslation.LocaleRequired);
        }

        var locale = localeCode.Trim();
        var existing = _translations.FirstOrDefault(t => t.LocaleCode == locale);

        if (existing is not null)
        {
            var renameResult = existing.Rename(name);
            if (renameResult.IsFailure)
            {
                return renameResult;
            }
        }
        else
        {
            var created = ProductTranslation.Create(locale, name, "human");
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _translations.Add(created.Value);
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Applies General-tab edits as one atomic change.
    ///
    /// <paramref name="expectedRowVersion"/> is the value the editor loaded;
    /// a mismatch means someone else saved in the meantime, so nothing is
    /// mutated and the caller gets a conflict. This is a domain concern, not
    /// just an EF concern: the rule is "last writer must have seen the latest
    /// state", regardless of persistence technology.
    /// </summary>
    public Result UpdateGeneral(
        string name,
        string? ownerDisplayName,
        string? gtin,
        string? sapMaterialNo,
        int expectedRowVersion)
    {
        if (expectedRowVersion != RowVersion)
        {
            return Result.Failure(ProductErrors.ConcurrentModification);
        }

        // Validate everything before mutating anything.
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result.Failure(ProductErrors.NameRequired);
        }

        if (trimmedName.Length > ProductTranslation.MaxNameLength)
        {
            return Result.Failure(ProductErrors.NameTooLong);
        }

        var nameResult = SetName(BaseLocale, trimmedName);
        if (nameResult.IsFailure)
        {
            return nameResult;
        }

        OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? null : ownerDisplayName.Trim();
        Gtin = string.IsNullOrWhiteSpace(gtin) ? null : gtin.Trim();
        SapMaterialNo = string.IsNullOrWhiteSpace(sapMaterialNo) ? null : sapMaterialNo.Trim();

        Touch();

        return Result.Success();
    }

    /// <summary>
    /// Adds a variant. Codes are unique per product (and globally, by the
    /// database index); the in-aggregate check gives a friendly error before
    /// the constraint fires.
    /// </summary>
    public Result<Sku> AddSku(string code, string? gtin, string? erpMaterialNo, int expectedRowVersion)
    {
        if (expectedRowVersion != RowVersion)
        {
            return Result.Failure<Sku>(ProductErrors.ConcurrentModification);
        }

        var created = Sku.Create(code, gtin, erpMaterialNo, NextSortOrder());
        if (created.IsFailure)
        {
            return created;
        }

        if (_skus.Any(s => string.Equals(s.Code, created.Value.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<Sku>(SkuErrors.DuplicateCode);
        }

        _skus.Add(created.Value);
        Touch();

        return created.Value;
    }

    /// <summary>Updates a variant's commercial fields. The code is immutable.</summary>
    public Result UpdateSku(
        string code,
        string? gtin,
        string? erpMaterialNo,
        SkuStatus status,
        int expectedRowVersion)
    {
        if (expectedRowVersion != RowVersion)
        {
            return Result.Failure(ProductErrors.ConcurrentModification);
        }

        var sku = FindSku(code);
        if (sku is null)
        {
            return Result.Failure(SkuErrors.NotFound(code));
        }

        var updated = sku.Update(gtin, erpMaterialNo, status, sku.SortOrder);
        if (updated.IsFailure)
        {
            return updated;
        }

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Removes a variant outright. Deliberately a hard delete: an unwanted SKU
    /// that was never distributed should leave no trace. Retiring a SKU that
    /// HAS been distributed is a status change to EndOfLife, not a removal.
    /// </summary>
    public Result RemoveSku(string code, int expectedRowVersion)
    {
        if (expectedRowVersion != RowVersion)
        {
            return Result.Failure(ProductErrors.ConcurrentModification);
        }

        var sku = FindSku(code);
        if (sku is null)
        {
            return Result.Failure(SkuErrors.NotFound(code));
        }

        _skus.Remove(sku);
        Touch();
        return Result.Success();
    }

    private Sku? FindSku(string code) =>
        _skus.FirstOrDefault(s =>
            string.Equals(s.Code, code?.Trim().ToUpperInvariant(), StringComparison.Ordinal));

    private int NextSortOrder() => _skus.Count == 0 ? 1 : _skus.Max(s => s.SortOrder) + 1;

    private void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion++;
    }

    /// <summary>
    /// Editorial workflow: Draft -> Review -> Approval -> Published, with
    /// rejection back to Draft from Review or Approval. Published is terminal
    /// until a republish flow exists.
    /// </summary>
    /// <summary>
    /// Moves the product through the editorial pipeline.
    ///
    /// The permitted moves are no longer a table inside this class. Up to
    /// Sprint 26 they were, which made an organisation's editorial process an
    /// invariant of a product — and meant the same rule existed a second time
    /// in the React app and a third time as reference data nobody had written
    /// yet. The invariant that stays here is the one that genuinely belongs to
    /// the aggregate: a product cannot be moved to an arbitrary state, only to
    /// one the configured pipeline permits from where it stands.
    /// </summary>
    public Result ChangeWorkflowState(WorkflowState target, IWorkflowPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (target == WorkflowState)
        {
            return Result.Success();
        }

        if (!policy.Allows(WorkflowState, target))
        {
            return Result.Failure(ProductErrors.InvalidWorkflowTransition(WorkflowState, target));
        }

        WorkflowState = target;
        Touch();

        return Result.Success();
    }

    public Result ChangeLifecycleState(LifecycleState target)
    {
        if (target == LifecycleState)
        {
            return Result.Success();
        }

        if (!AllowedTransitions[LifecycleState].Contains(target))
        {
            return Result.Failure(ProductErrors.InvalidLifecycleTransition(LifecycleState, target));
        }

        var previous = LifecycleState;
        LifecycleState = target;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        RaiseDomainEvent(new ProductLifecycleChangedDomainEvent(PublicId, previous, target));
        return Result.Success();
    }
}
