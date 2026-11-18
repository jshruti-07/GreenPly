using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Products;

/// <summary>
/// Localized product text (`product_tr`). Part of the Product aggregate —
/// never loaded or modified independently. Composite key (product_id,
/// locale_code) means this is not an <c>Entity</c> with a surrogate id.
///
/// This slice carries only <c>name</c>; the remaining columns (short_desc,
/// long_desc, marketing, seo_*, slug) arrive with the detail screen's
/// General tab.
/// </summary>
public sealed class ProductTranslation
{
    public const int MaxNameLength = 300;

    private ProductTranslation()
    {
        // EF Core materialization only.
        LocaleCode = null!;
        Name = null!;
        TranslationStatus = null!;
    }

    public long ProductId { get; private set; }

    public string LocaleCode { get; private set; }

    public string Name { get; private set; }

    /// <summary>missing · machine · human · approved (product_tr.translation_status).</summary>
    public string TranslationStatus { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Declared once and referenced from both call sites. The same code and
    /// message were previously written out in two places, which is how a code
    /// and its wording quietly drift apart.
    /// </summary>
    public static readonly Error LocaleRequired =
        Error.Validation("Product.Translation.Locale.Required", "A locale code is required.");

    internal static Result<ProductTranslation> Create(string localeCode, string name, string translationStatus)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return Result.Failure<ProductTranslation>(LocaleRequired);
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result.Failure<ProductTranslation>(ProductErrors.NameRequired);
        }

        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Failure<ProductTranslation>(ProductErrors.NameTooLong);
        }

        return new ProductTranslation
        {
            LocaleCode = localeCode.Trim(),
            Name = trimmedName,
            TranslationStatus = translationStatus,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    internal Result Rename(string name)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result.Failure(ProductErrors.NameRequired);
        }

        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Failure(ProductErrors.NameTooLong);
        }

        Name = trimmedName;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }
}
