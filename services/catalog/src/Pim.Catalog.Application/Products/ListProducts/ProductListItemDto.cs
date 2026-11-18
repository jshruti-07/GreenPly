namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>
/// One row of the products grid (v4.2 columns). CompletenessPct follows the
/// DB design's computed rule: approved values for required attributes ÷
/// required attributes in the product's attribute set (0 when no set).
/// Owner is the interim display-text column (owner_display_name), replaced by
/// a real identity reference when auth ships (decision 2026-08-14).
/// </summary>
public sealed record ProductListItemDto(
    Guid PublicId,
    string Code,
    string Name,
    string Category,
    string LifecycleState,
    string WorkflowState,
    int CompletenessPct,
    IReadOnlyList<string> Languages,
    int VariantCount,
    string? Owner,
    DateTimeOffset UpdatedAtUtc,
    /// <summary>Derived placeholder — see <see cref="ChannelReadiness"/>.</summary>
    IReadOnlyList<ChannelReadinessLevel> ChannelReadiness,
    /// <summary>
    /// The locale <see cref="Name"/> was actually read from. Equal to the
    /// requested display locale normally; the product's base locale when no
    /// translation existed and the row fell back.
    ///
    /// Carried per row rather than inferred client-side because the client
    /// cannot know: it asked for one locale and got text back, and whether that
    /// text is a translation or a substitute is exactly the fact a PIM exists
    /// to surface. The UI marks the difference (decision 2026-08-17).
    /// </summary>
    /// Null means no translation existed in either locale and the product code
    /// is standing in for a name — a different and worse state than a fallback.
    string? NameLocale,
    /// <summary>As <see cref="NameLocale"/>, for the category label.</summary>
    string? CategoryLocale);
