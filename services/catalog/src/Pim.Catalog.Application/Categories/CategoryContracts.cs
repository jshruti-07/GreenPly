using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Categories;

/// <summary>
/// One node of the merchandising tree, with its children nested beneath it.
///
/// Two product counts, deliberately. <paramref name="DirectProductCount"/> is
/// the products whose primary node IS this node; <paramref name="SubtreeProductCount"/>
/// includes every descendant. A tree that shows only one of them is read as the
/// other: a Family with 0 direct products and 300 beneath it looks empty, and a
/// leaf showing the subtree figure looks like it owns products it does not.
/// v4.2 shows a single count per node, which is unambiguous only because its
/// demo tree has products at the leaves only.
/// </summary>
public sealed record CategoryTreeNodeDto(
    long Id,
    string Code,
    string Name,
    // The locale the name actually came from, null when the node has no
    // translation at all. Same shape the products grid returns, so the screens
    // share one FallbackMark component and one definition of "this is a gap"
    // instead of each inventing a boolean.
    string? NameLocale,
    string NodeType,
    int Depth,
    string Status,
    int DirectProductCount,
    int SubtreeProductCount,
    IReadOnlyList<CategoryTreeNodeDto> Children);

/// <summary>
/// One row of the attribute template attached to a category.
///
/// <paramref name="CoveragePct"/> is the share of products in this node's
/// SUBTREE that carry an approved value for the attribute — the same rule the
/// grid and the cockpit use for completeness, so the three cannot disagree.
/// <paramref name="ProductsInScope"/> travels with it because a coverage
/// percentage over four products means something very different from the same
/// percentage over four hundred, and the reader cannot tell which they are
/// looking at from the bar alone.
/// </summary>
public sealed record AttributeTemplateRowDto(
    string Code,
    string Label,
    string? LabelLocale,
    string GroupCode,
    string GroupLabel,
    string DataType,
    bool IsRequired,
    int CoveragePct,
    int ProductsInScope);

/// <summary>An attribute set the products beneath a node were created with.</summary>
public sealed record AttributeSetUsageDto(string Code, string Name, int ProductCount);

/// <summary>
/// Where the template on screen came from.
///
/// This distinction is the whole reason this field exists. `taxonomy_node`
/// carries an `attribute_set_id` — a template DECLARED for everything beneath
/// the node — and the client's catalogue does not populate it on a single node.
/// A panel that only ever read that column would be empty forever, and an empty
/// "Attribute template" panel reads as "this branch demands nothing".
///
/// So when nothing is declared, the screen falls back to the set the products
/// underneath actually use, and says which of the two it is showing. It refuses
/// to merge several sets: combining two templates needs a rule for an attribute
/// that is required in one and optional in the other, and nobody has specified
/// one. In that case the sets are listed and the table is withheld.
/// </summary>
public static class TemplateSource
{
    /// <summary>No template, and no products underneath to infer one from.</summary>
    public const string None = "none";

    /// <summary>The set declared on the node itself (taxonomy_node.attribute_set_id).</summary>
    public const string Declared = "declared";

    /// <summary>Not declared anywhere; this is the one set every product beneath uses.</summary>
    public const string InUse = "in-use";

    /// <summary>Several sets are in use beneath this node, so no single template applies.</summary>
    public const string Mixed = "mixed";
}

/// <summary>
/// The right-hand panel of v4.2's Categories screen: what this node is, and
/// what its attribute template demands of the products underneath it.
/// </summary>
public sealed record CategoryDetailDto(
    long Id,
    string Code,
    string Name,
    string? NameLocale,
    string? Description,
    string NodeType,
    int Depth,
    string Status,
    string? ParentCode,
    string? ParentName,
    string? BrandName,
    int DirectProductCount,
    int SubtreeProductCount,
    // Null when no attribute set is attached — which is not the same as an
    // empty template, and the screen renders the two differently.
    string? AttributeSetCode,
    string? AttributeSetName,
    int RequiredAttributeCount,
    // Declared on the node, inferred from its products, or neither — stated
    // rather than left for the reader to assume. One of TemplateSource's
    // constants. A string, not an enum: this API has no string-enum converter
    // configured, so an enum would go over the wire as 2 and the screen would
    // have to know that 2 means "in-use".
    string TemplateSource,
    IReadOnlyList<AttributeSetUsageDto> SetsInUse,
    IReadOnlyList<AttributeTemplateRowDto> Template,
    // The node's label in every configured locale, so the screen can show what
    // a French reader would see without switching language. v4.2 has a
    // hard-coded "Label (DE)" field; this is the same idea without assuming
    // which two languages the catalogue happens to have.
    IReadOnlyList<CategoryLabelDto> Labels);

/// <summary>A node's label in one locale, and whether that locale has one at all.</summary>
public sealed record CategoryLabelDto(
    string LocaleCode,
    string LocaleName,
    bool IsBase,
    string? Name,
    string TranslationStatus);

public sealed record GetCategoryTreeQuery(string? Locale = null)
    : IRequest<Result<IReadOnlyList<CategoryTreeNodeDto>>>;

public sealed record GetCategoryDetailQuery(string Code, string? Locale = null)
    : IRequest<Result<CategoryDetailDto>>;

/// <summary>Read-side port for the Categories screen.</summary>
public interface ICategoryStore
{
    Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(
        DisplayLocale locale, CancellationToken cancellationToken);

    /// <summary>Null when no active node carries that code.</summary>
    Task<CategoryDetailDto?> GetDetailAsync(
        string code, DisplayLocale locale, CancellationToken cancellationToken);
}
