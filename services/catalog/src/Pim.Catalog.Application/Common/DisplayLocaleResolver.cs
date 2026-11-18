using Pim.Catalog.Application.Lookups;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Common;

/// <summary>
/// Resolves a requested display locale, or fails with a coded error.
///
/// Extracted because five detail handlers plus the grid and the export all need
/// the same three lines, and the failure branch is the part that would rot: a
/// handler that forgot to reject an unconfigured locale would silently render
/// the default instead, which is precisely the behaviour ADR-009 rules out.
/// One implementation means one place for that rule to be true.
/// </summary>
public sealed class DisplayLocaleResolver(ILocaleCatalog locales)
{
    public async Task<Result<DisplayLocale>> ResolveAsync(
        string? requested,
        CancellationToken cancellationToken)
    {
        var configured = await locales.GetAllAsync(cancellationToken);
        var resolved = DisplayLocale.Resolve(requested, configured);

        return resolved is null
            ? Result.Failure<DisplayLocale>(LocaleErrors.Unknown(requested!))
            : Result.Success(resolved);
    }
}
