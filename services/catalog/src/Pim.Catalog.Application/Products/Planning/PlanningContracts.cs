using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Planning;

/// <summary>
/// One planning parameter as a specifier reads it: label, value, unit.
///
/// These are ordinary attributes from the 'planning' group, not a separate
/// property-set entity (decision 2026-08-16). <paramref name="Code"/> is
/// included so the tab can deep-link the steward to the Attributes tab to
/// change it — this tab does not write.
/// </summary>
public sealed record PlanningParameterDto(
    string Code,
    string Label,
    string? Value,
    string? UnitSymbol,
    string DataType);

/// <summary>
/// A BIM classification reference. IFC, OmniClass and Uniclass are ordinary
/// classification schemes, so a reference here is the same row the
/// Classifications tab shows — filtered, not duplicated.
/// </summary>
public sealed record BimReferenceDto(
    string SchemeCode,
    string SchemeName,
    string SchemeVersion,
    string? ClassCode,
    string? ClassLabel);

/// <summary>A geometry or planning file: an asset whose type is BIM or CAD.</summary>
public sealed record GeometryFileDto(
    long LinkId,
    string Role,
    string AssetType,
    string? FileName,
    long? FileSize,
    string Url,
    string? Title,
    bool IsExternal);

/// <summary>
/// The Planning &amp; BIM tab.
///
/// Composition, not a new module: parameters come from the attribute engine,
/// references from the classification module, files from the asset module.
/// That is why there is no write side here — a planner changing a value uses
/// the Attributes tab, a class assignment uses Classifications, a file uses
/// Media &amp; Documents. Adding a fourth write path onto the same three tables
/// would have meant three more concurrency implementations to keep in step.
///
/// <paramref name="BimReady"/> is the client's own BIM_Generator flag. There is
/// no "publish to BIM" action: that needs the channel service, which does not
/// exist (ADR-004), and a button that only shows a toast is worse than no
/// button.
/// </summary>
public sealed record ProductPlanningDto(
    string ProductCode,
    int RowVersion,
    bool? BimReady,
    int ParameterCount,
    int ParameterFilledCount,
    IReadOnlyList<PlanningParameterDto> Parameters,
    IReadOnlyList<BimReferenceDto> BimReferences,
    IReadOnlyList<GeometryFileDto> GeometryFiles);

public sealed record GetProductPlanningQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductPlanningDto>>;

/// <summary>Read port for the Planning tab; SQL lives in Infrastructure.</summary>
public interface IProductPlanningStore
{
    Task<ProductPlanningDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken);
}
