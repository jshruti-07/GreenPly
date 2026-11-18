using Pim.Catalog.Application.Lookups;

namespace Pim.Catalog.Application.Common;

/// <summary>
/// The locale a read should be rendered in, resolved against the locales the
/// catalogue is actually configured for.
///
/// This is a *view* concern and nothing else. It never changes a product's
/// `base_locale`, never decides which translations exist, and is not persisted
/// anywhere — switching the selector is the same kind of act as sorting a
/// column.
/// </summary>
public sealed record DisplayLocale(string Code)
{
    /// <summary>
    /// What is shown when the caller asks for nothing.
    ///
    /// English, by product decision on 2026-08-17: the catalogue's base locale
    /// is de-DE because the client's master export is German, but the people
    /// working in this tool read English. Base locale is a property of the
    /// data; default display locale is a property of the audience, and
    /// conflating them is why the grid was German for fifteen sprints.
    /// </summary>
    public const string PreferredDefault = "en-GB";

    /// <summary>
    /// Resolves a requested code to the spelling stored in `catalog.locale`.
    ///
    /// Returns null when the caller asked for a locale this catalogue does not
    /// have. That is a rejection, not a fallback: see
    /// <see cref="LocaleErrors.Unknown"/>.
    /// </summary>
    /// <param name="requested">
    /// Null or blank means "no preference" and resolves to the default chain.
    /// </param>
    public static DisplayLocale? Resolve(string? requested, IReadOnlyList<LocaleDto> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        if (configured.Count == 0)
        {
            // A catalogue with no locales cannot render a name in any language.
            // Better to say so here than to emit SQL that silently matches
            // nothing and looks like missing data.
            throw new InvalidOperationException(
                "No locales are configured; catalog.locale is empty.");
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            // Case-insensitive match, canonical spelling out: BCP 47 tags are
            // case-insensitive, but the SQL compares against the stored value.
            var match = configured.FirstOrDefault(
                l => string.Equals(l.Code, requested.Trim(), StringComparison.OrdinalIgnoreCase));

            return match is null ? null : new DisplayLocale(match.Code);
        }

        return new DisplayLocale(DefaultCode(configured));
    }

    /// <summary>
    /// The default chain: the preferred default if configured, else any other
    /// English variant, else the base locale, else the first locale there is.
    ///
    /// The second step matters — a catalogue set up with en-US rather than
    /// en-GB should still open in English rather than silently reverting to
    /// German because one tag did not match.
    /// </summary>
    private static string DefaultCode(IReadOnlyList<LocaleDto> configured)
    {
        var preferred = configured.FirstOrDefault(
            l => string.Equals(l.Code, PreferredDefault, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            return preferred.Code;
        }

        var anyEnglish = configured.FirstOrDefault(
            l => l.Code.StartsWith("en-", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(l.Code, "en", StringComparison.OrdinalIgnoreCase));
        if (anyEnglish is not null)
        {
            return anyEnglish.Code;
        }

        return configured.FirstOrDefault(l => l.IsBase)?.Code ?? configured[0].Code;
    }
}
