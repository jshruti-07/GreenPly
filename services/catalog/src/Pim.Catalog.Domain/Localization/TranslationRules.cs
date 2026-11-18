using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Localization;

/// <summary>
/// The states a translation can be in.
///
/// 'missing' is derived, never set by a caller: it means the row does not
/// exist yet. Letting a client post 'missing' would let it assert a state the
/// data contradicts — the same reason 'expired' is not settable on a
/// compliance document.
/// </summary>
public static class TranslationStatus
{
    public const string Missing = "missing";
    public const string Pending = "pending";
    public const string Translated = "translated";
    public const string Approved = "approved";

    /// <summary>Every state the read model can report.</summary>
    public static readonly IReadOnlyList<string> All =
        [Missing, Pending, Translated, Approved];

    /// <summary>The subset a caller may assign.</summary>
    public static readonly IReadOnlyList<string> Settable =
        [Pending, Translated, Approved];

    public static bool IsSettable(string value) => Settable.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// Which fields the Localization tab may write, and how they are addressed.
///
/// The whitelist exists because the field name reaches a column name. Product
/// fields map to <c>product_tr</c> columns; anything else is treated as an
/// attribute code and must resolve to an attribute marked
/// <c>is_localizable</c>. Neither path interpolates caller input into SQL.
/// </summary>
public static class TranslationRules
{
    public const int MaxLocaleLength = 12;
    public const int MaxValueLength = 4000;

    /// <summary>
    /// Product-level translatable columns, in the order the tab shows them:
    /// the name first because it is the only required one, then the
    /// descriptive copy, then SEO.
    /// </summary>
    public static readonly IReadOnlyList<string> ProductFields =
        ["name", "shortDesc", "longDesc", "marketing", "seoTitle", "seoDesc"];

    /// <summary>Field code to the physical <c>product_tr</c> column.</summary>
    private static readonly Dictionary<string, string> Columns = new(StringComparer.Ordinal)
    {
        ["name"] = "name",
        ["shortDesc"] = "short_desc",
        ["longDesc"] = "long_desc",
        ["marketing"] = "marketing",
        ["seoTitle"] = "seo_title",
        ["seoDesc"] = "seo_desc",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["name"] = "Product name",
        ["shortDesc"] = "Short description",
        ["longDesc"] = "Long description",
        ["marketing"] = "Marketing copy",
        ["seoTitle"] = "SEO title",
        ["seoDesc"] = "SEO description",
    };

    /// <summary>Fields rendered as a textarea rather than a single-line input.</summary>
    private static readonly HashSet<string> Multiline = new(StringComparer.Ordinal)
    {
        "shortDesc", "longDesc", "marketing", "seoDesc",
    };

    public static bool IsProductField(string field) => Columns.ContainsKey(field);

    /// <summary>
    /// True for a product field or anything that could be an attribute code.
    /// The attribute must still exist and be localizable — that is checked
    /// against the database, not here, because only the database knows.
    /// </summary>
    public static bool IsKnownField(string field) =>
        !string.IsNullOrWhiteSpace(field) &&
        (IsProductField(field) || IsAttributeCodeShaped(field));

    public static string ColumnFor(string field) => Columns[field];

    public static string LabelFor(string field) =>
        Labels.TryGetValue(field, out var label) ? label : field;

    public static bool IsMultiline(string field) => Multiline.Contains(field);

    /// <summary>The product name is the one field a locale cannot be without.</summary>
    public static bool IsRequired(string field) => string.Equals(field, "name", StringComparison.Ordinal);

    /// <summary>
    /// Attribute codes are lower snake_case, as emitted by the seed generator
    /// and enforced by the attribute library. Shape-checking here keeps an
    /// obviously malformed field out of the store, where it would otherwise
    /// come back as a confusing "attribute not found".
    /// </summary>
    private static bool IsAttributeCodeShaped(string field) =>
        field.Length <= 64 && field.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}

public static class LocalizationErrors
{
    public static Error UnknownLocale(string locale) =>
        Error.NotFound("Localization.Locale.NotFound",
            $"Locale '{locale}' is not configured for this catalogue.");

    public static Error BaseLocaleNotEditable(string locale) =>
        Error.Validation("Localization.BaseLocale.NotEditable",
            $"'{locale}' is the product's base locale and is edited on the General and Attributes tabs, not here.");

    public static Error NotLocalizable(string attributeCode) =>
        Error.Validation("Localization.Attribute.NotLocalizable",
            $"Attribute '{attributeCode}' is not localizable, so it has one value for every language.");

    public static Error UnknownField(string field) =>
        Error.Validation("Localization.Field.NotTranslatable",
            $"'{field}' is not a translatable field on this product.");

    public static Error RequiredFieldCleared(string field) =>
        Error.Validation("Localization.Field.Required",
            $"'{TranslationRules.LabelFor(field)}' is required and cannot be cleared.");
}
