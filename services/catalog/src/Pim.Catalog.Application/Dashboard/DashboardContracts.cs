using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Dashboard;

/// <summary>One headline figure on the cockpit.</summary>
public sealed record DashboardKpiDto(string Code, string Label, string Value, string? Caption);

/// <summary>A quality band — v4.2's A/B/C/D breakdown of the catalogue.</summary>
public sealed record QualityBandDto(string Code, string Label, int ProductCount, int Percent);

/// <summary>Average completeness for one attribute group, e.g. 'technical'.</summary>
public sealed record CompletenessByAreaDto(string GroupCode, string Label, int Percent);

/// <summary>How far each configured locale has been translated.</summary>
public sealed record LocaleCoverageDto(
    string Code,
    string Name,
    bool IsBase,
    int TranslatedProducts,
    int TotalProducts,
    int Percent);

/// <summary>Where products sit in the editorial pipeline.</summary>
public sealed record WorkflowStageDto(string State, string Label, int ProductCount);

/// <summary>One line of the activity feed, read from the audit trail.</summary>
public sealed record DashboardActivityDto(
    DateTimeOffset OccurredAtUtc,
    string? Actor,
    string Action,
    string ProductCode,
    string? Detail);

/// <summary>
/// The Product Cockpit, v4.2's first screen and the first item in the Phase 1
/// estimate.
///
/// WHAT IS DELIBERATELY ABSENT, and why it is absent rather than approximated:
///
///   - <b>Channel errors and channel sync status.</b> There is no channel
///     service. Numbers here would be invented, and a dashboard is exactly the
///     place where an invented number is believed — it is read at a glance, by
///     people who did not build it, and quoted in meetings.
///   - <b>The twelve-week completeness trend.</b> Nothing snapshots completeness
///     over time. The audit trail records what changed, not what the catalogue
///     scored on a given Tuesday, so the line could only be drawn by
///     back-filling a shape that looks plausible.
///   - <b>"My work queue".</b> Two things missing, not one: there is no workflow
///     engine to produce tasks, and no authentication, so there is no "my".
///
/// Each omission is surfaced on the screen with its reason. A cockpit with four
/// honest panels and three stated gaps is more useful than one with seven
/// panels where the reader cannot tell which three are real.
/// </summary>
public sealed record ProductCockpitDto(
    IReadOnlyList<DashboardKpiDto> Kpis,
    IReadOnlyList<QualityBandDto> QualityBands,
    IReadOnlyList<CompletenessByAreaDto> CompletenessByArea,
    IReadOnlyList<WorkflowStageDto> WorkflowPipeline,
    IReadOnlyList<LocaleCoverageDto> LocaleCoverage,
    IReadOnlyList<DashboardActivityDto> RecentActivity,
    /// <summary>
    /// Panels v4.2 specifies that this build does not render, each with the
    /// reason. Carried in the payload rather than hard-coded in the UI so the
    /// gap is part of the contract: when the channel service lands, the panel
    /// appears because the reason disappears, and nobody has to remember to
    /// delete a placeholder from a React component.
    /// </summary>
    IReadOnlyList<OmittedPanelDto> OmittedPanels);

/// <summary>A v4.2 panel that is not rendered, and the reason it is not.</summary>
public sealed record OmittedPanelDto(string Panel, string Reason);

public sealed record GetProductCockpitQuery(string? Locale = null)
    : IRequest<Result<ProductCockpitDto>>;

/// <summary>Read port for the cockpit; SQL lives in Infrastructure.</summary>
public interface IDashboardStore
{
    Task<ProductCockpitDto> GetAsync(DisplayLocale locale, CancellationToken cancellationToken);
}
