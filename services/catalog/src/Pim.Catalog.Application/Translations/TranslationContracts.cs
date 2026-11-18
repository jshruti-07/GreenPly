using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Translations;

/// <summary>
/// The derived state of one product's text in one target locale.
///
/// Two of these are computed here and the rest are passed through from
/// `product_tr.translation_status` untouched. That split is deliberate:
/// "missing" and "outdated" are facts about the ROW's existence and age that no
/// column records, while the status vocabulary itself belongs to whoever writes
/// the data. The schema comment says missing/machine/human/approved and the
/// client's seed actually contains approved/pending/draft — inventing a
/// canonical list here would mean silently reclassifying rows this service does
/// not own.
/// </summary>
public static class TranslationState
{
    /// <summary>No product_tr row at all for that locale.</summary>
    public const string Missing = "missing";

    /// <summary>A row exists, but the base-locale text has been edited since.</summary>
    public const string Outdated = "outdated";
}

/// <summary>How far one locale has got, and what is left to do in it.</summary>
public sealed record LocaleProgressDto(
    string Code,
    string Name,
    bool IsBase,
    int TotalProducts,
    // Products with a product_tr row in this locale, whatever its status. Same
    // definition the dashboard's coverage panel uses — the two are cross-checked
    // against each other in the smoke suite, because two screens reporting
    // different translation coverage would make both worthless.
    int WithText,
    int Missing,
    int Outdated,
    int AwaitingReview,
    int Approved,
    int PercentTranslated);

/// <summary>One row of the review queue.</summary>
public sealed record TranslationQueueItemDto(
    string ProductCode,
    string Name,
    string? NameLocale,
    string Category,
    string State,
    // The stored status, kept beside the derived state rather than folded into
    // it. A row can be 'approved' and outdated at once, and collapsing the two
    // would hide which of the two problems it is.
    string? TranslationStatus,
    string? Source,
    DateTimeOffset? TargetUpdatedAtUtc,
    DateTimeOffset? BaseUpdatedAtUtc);

public sealed record TranslationQueueDto(
    string TargetLocale,
    string State,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<TranslationQueueItemDto> Items);

/// <summary>A panel v4.2 draws that this build cannot honestly populate.</summary>
public sealed record TranslationOmittedPanelDto(string Panel, string Reason);

/// <summary>
/// The Translation Workbench.
///
/// Every figure on this screen comes from ONE per-locale query, and the KPI row
/// is summed from the same rows the table below it shows. A separate KPI query
/// would let the headline and the breakdown disagree — which on this screen
/// means telling someone there are 100 products to translate and then listing
/// 97.
/// </summary>
public sealed record TranslationWorkbenchDto(
    string BaseLocaleCode,
    string BaseLocaleName,
    int TotalProducts,
    IReadOnlyList<LocaleProgressDto> Locales,
    IReadOnlyList<TranslationOmittedPanelDto> OmittedPanels);

public sealed record GetTranslationWorkbenchQuery(string? Locale = null)
    : IRequest<Result<TranslationWorkbenchDto>>;

/// <summary>
/// The queue for one target locale.
/// </summary>
/// <param name="Target">The locale being translated INTO.</param>
/// <param name="State">
/// Which rows to return: "attention" (missing, outdated or not yet approved —
/// the default, because that is what a work queue is for), "missing",
/// "outdated", "review", or "all".
/// </param>
/// <param name="Locale">The locale the ROW TEXT renders in — not the target.</param>
public sealed record GetTranslationQueueQuery(
    string Target,
    string? State = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25,
    string? Locale = null)
    : IRequest<Result<TranslationQueueDto>>;

public interface ITranslationWorkbenchStore
{
    Task<TranslationWorkbenchDto> GetWorkbenchAsync(
        DisplayLocale locale, CancellationToken cancellationToken);

    Task<TranslationQueueDto> GetQueueAsync(
        string target,
        string state,
        string? search,
        int page,
        int pageSize,
        DisplayLocale locale,
        CancellationToken cancellationToken);
}
