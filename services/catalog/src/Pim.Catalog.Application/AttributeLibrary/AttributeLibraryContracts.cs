using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AttributeLibrary;

/// <summary>
/// An attribute group, with how many attributes sit in it.
///
/// Separate namespace from <c>Pim.Catalog.Application.Attributes</c>, which is
/// about one product's VALUES. This is the library: the definitions those
/// values are instances of. Merging the two namespaces would put
/// "what is this product's fire rating" next to "what kind of thing is a fire
/// rating", and they are answered by different screens for different people.
/// </summary>
public sealed record AttributeGroupSummaryDto(
    string Code,
    string Label,
    string? LabelLocale,
    int AttributeCount);

/// <summary>
/// One row of the library.
///
/// <paramref name="AppliesToProducts"/> is the denominator that makes
/// <paramref name="CoveragePct"/> mean anything: the products whose attribute
/// set actually includes this attribute. Coverage over the whole catalogue
/// would make every specialised attribute look abandoned, and the number would
/// quietly punish exactly the attributes that are correctly scoped.
/// </summary>
public sealed record AttributeSummaryDto(
    string Code,
    string Label,
    string? LabelLocale,
    string GroupCode,
    string GroupLabel,
    string DataType,
    string? Unit,
    bool IsLocalizable,
    bool IsVariantAxis,
    string Scope,
    string SourceAuthority,
    int OptionCount,
    int SetCount,
    bool RequiredInAnySet,
    int AppliesToProducts,
    int ProductsWithValue,
    int CoveragePct);

/// <summary>The library screen: groups down the side, attributes in the table.</summary>
public sealed record AttributeLibraryDto(
    IReadOnlyList<AttributeGroupSummaryDto> Groups,
    IReadOnlyList<AttributeSummaryDto> Attributes,
    // Every attribute in one payload, deliberately: the catalogue defines a few
    // dozen and the screen filters client-side. Stated here so that when the
    // library grows past a few hundred this is a decision to revisit rather
    // than a silent limit somebody hits.
    int TotalAttributes);

/// <summary>An attribute's text in one locale — the row the editor will write.</summary>
public sealed record AttributeLabelDto(
    string LocaleCode,
    string LocaleName,
    bool IsBase,
    string? Label,
    string? HelpText,
    string? Placeholder);

/// <summary>One selectable option of a list attribute, per locale.</summary>
public sealed record AttributeOptionDetailDto(
    string Code,
    string Label,
    string? LabelLocale,
    int SortOrder,
    int UsedByProducts);

/// <summary>An attribute set that includes this attribute, and on what terms.</summary>
public sealed record AttributeSetMembershipDto(
    string SetCode,
    string SetName,
    bool IsRequired,
    int SortOrder,
    int ProductCount);

/// <summary>Everything one attribute is: definition, text, options, membership.</summary>
public sealed record AttributeDetailDto(
    string Code,
    string Label,
    string? LabelLocale,
    string GroupCode,
    string GroupLabel,
    string DataType,
    string? Unit,
    string? UnitCode,
    bool IsLocalizable,
    bool IsVariantAxis,
    string Scope,
    string SourceAuthority,
    // Raw validation JSON as stored. Rendered as-is rather than interpreted:
    // this build has no schema for it, and a pretty summary of a rule nobody
    // has specified would be a guess presented as documentation.
    string? ValidationJson,
    int AppliesToProducts,
    int ProductsWithValue,
    int CoveragePct,
    IReadOnlyList<AttributeLabelDto> Labels,
    IReadOnlyList<AttributeOptionDetailDto> Options,
    IReadOnlyList<AttributeSetMembershipDto> Sets);

public sealed record GetAttributeLibraryQuery(string? Locale = null)
    : IRequest<Result<AttributeLibraryDto>>;

public sealed record GetAttributeDetailQuery(string Code, string? Locale = null)
    : IRequest<Result<AttributeDetailDto>>;

public interface IAttributeLibraryStore
{
    Task<AttributeLibraryDto> GetLibraryAsync(
        DisplayLocale locale, CancellationToken cancellationToken);

    /// <summary>Null when no attribute carries that code.</summary>
    Task<AttributeDetailDto?> GetDetailAsync(
        string code, DisplayLocale locale, CancellationToken cancellationToken);
}
