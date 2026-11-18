using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Attributes;

/// <summary>One selectable option of a list attribute.</summary>
public sealed record AttributeOptionDto(string Code, string Label);

/// <summary>
/// An attribute as rendered in the form: its definition, whether the tab can
/// edit it, and the product's current value (canonical string form).
/// </summary>
public sealed record ProductAttributeDto(
    string Code,
    /// <summary>
    /// Translated into the product's base locale, falling back to the code.
    /// The code is still sent because it is what writes are addressed by; the
    /// UI must never show it.
    /// </summary>
    string Label,
    string DataType,
    bool IsRequired,
    bool IsLocalizable,
    bool IsEditable,
    string? UnitSymbol,
    string? Value,
    string? DataState,
    IReadOnlyList<AttributeOptionDto> Options);

/// <summary>Attributes grouped by attribute_group, in the group's sort order.</summary>
public sealed record ProductAttributeGroupDto(
    string Code,
    string Name,
    IReadOnlyList<ProductAttributeDto> Attributes);

/// <summary>
/// The full Attributes tab payload. CompletenessPct is recomputed from the
/// same rule the grid uses, so saving a value and seeing the bar move needs
/// no second request.
/// </summary>
public sealed record ProductAttributesDto(
    string ProductCode,
    string BaseLocale,
    int CompletenessPct,
    int RequiredCount,
    int RequiredFilledCount,
    int RowVersion,
    IReadOnlyList<ProductAttributeGroupDto> Groups);

public sealed record GetProductAttributesQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductAttributesDto>>;

/// <summary>A single edit. Null <paramref name="Value"/> clears the value.</summary>
public sealed record AttributeValueInput(string AttributeCode, string? Value);

public sealed record UpdateProductAttributesCommand(
    string Code,
    IReadOnlyList<AttributeValueInput> Values,
    int RowVersion,
    /// <summary>Locale the refreshed payload is rendered in; the form must not
    /// change language when it is saved.</summary>
    string? Locale = null) : IRequest<Result<ProductAttributesDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => Values.Count == 1
        ? $"Attribute {Values[0].AttributeCode}"
        : $"{Values.Count} attributes";
}

/// <summary>Read/write port for attribute values; SQL lives in Infrastructure.</summary>
public interface IProductAttributeStore
{
    Task<ProductAttributesDto?> GetAsync(
        string productCode,
        DisplayLocale displayLocale,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies validated writes in one transaction and returns the refreshed
    /// payload. Returns null when the product disappeared meanwhile.
    ///
    /// Takes the display locale because it returns a rendered payload, not just
    /// an acknowledgement: without it a save would hand back base-locale labels
    /// and the form would visibly flip language on submit.
    /// </summary>
    Task<Result<ProductAttributesDto>> SaveAsync(
        string productCode,
        IReadOnlyList<AttributeValueInput> values,
        int expectedRowVersion,
        DisplayLocale displayLocale,
        CancellationToken cancellationToken);
}
