using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Lookups;

/// <summary>One configured locale, as offered in the language selector.</summary>
public sealed record LocaleDto(string Code, string Name, bool IsBase);

/// <summary>
/// The locales this catalogue is configured for.
///
/// Separate from <see cref="ILookupReader"/> because the answer is needed on
/// every read request to validate the requested display locale, not only when
/// a wizard is open. Implementations are expected to cache: the locale list
/// changes approximately never, and a database round trip per grid request to
/// re-learn that would be a self-inflicted cost.
/// </summary>
public interface ILocaleCatalog
{
    Task<IReadOnlyList<LocaleDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The configured base locale, or null if none is flagged.</summary>
    Task<string?> GetBaseCodeAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken);
}

/// <summary>Coded failures for locale handling; rendered as RFC 7807.</summary>
public static class LocaleErrors
{
    /// <summary>
    /// Rejected rather than quietly ignored. Falling back to a default on an
    /// unrecognised locale produces the worst kind of bug report — "I selected
    /// French and it is still showing German" — with nothing in the response
    /// to explain it.
    /// </summary>
    public static Error Unknown(string code) => Error.Validation(
        "Locale.Unknown",
        $"'{code}' is not a configured locale for this catalogue.");
}
