using MediatR;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Translations;

public sealed class GetTranslationWorkbenchQueryHandler(
    ITranslationWorkbenchStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetTranslationWorkbenchQuery, Result<TranslationWorkbenchDto>>
{
    public async Task<Result<TranslationWorkbenchDto>> Handle(
        GetTranslationWorkbenchQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);

        return locale.IsFailure
            ? Result.Failure<TranslationWorkbenchDto>(locale.Error)
            : Result.Success(await store.GetWorkbenchAsync(locale.Value, cancellationToken));
    }
}

public sealed class GetTranslationQueueQueryHandler(
    ITranslationWorkbenchStore store,
    DisplayLocaleResolver localeResolver,
    ILocaleCatalog locales)
    : IRequestHandler<GetTranslationQueueQuery, Result<TranslationQueueDto>>
{
    /// <summary>
    /// Page size cap, matching the products grid.
    ///
    /// The same number in both places is not a coincidence to be maintained by
    /// hand — it is the point. A queue that quietly allowed 1,000 rows would be
    /// the one unpaged read in the service, found later under load.
    /// </summary>
    public const int MaxPageSize = 100;

    public async Task<Result<TranslationQueueDto>> Handle(
        GetTranslationQueueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            return Result.Failure<TranslationQueueDto>(TranslationErrors.TargetRequired);
        }

        if (request.Page < 1)
        {
            return Result.Failure<TranslationQueueDto>(TranslationErrors.PageOutOfRange);
        }

        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
        {
            return Result.Failure<TranslationQueueDto>(TranslationErrors.PageSizeOutOfRange(MaxPageSize));
        }

        var state = NormaliseState(request.State);
        if (state is null)
        {
            return Result.Failure<TranslationQueueDto>(TranslationErrors.UnknownState(request.State!));
        }

        // The target must be a locale this catalogue actually has. Resolved
        // against the same catalogue the selector reads, so a typo comes back as
        // a 400 naming the problem rather than an empty queue that reads as
        // "nothing left to translate" — which is the most misleading answer this
        // endpoint could give.
        var configured = await locales.GetAllAsync(cancellationToken);
        var target = configured.FirstOrDefault(
            l => string.Equals(l.Code, request.Target.Trim(), StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return Result.Failure<TranslationQueueDto>(LocaleErrors.Unknown(request.Target));
        }

        // Translating into the base locale is not a thing: the base locale IS
        // the source. Rejected rather than served as an empty list, for the same
        // reason as above.
        if (target.IsBase)
        {
            return Result.Failure<TranslationQueueDto>(TranslationErrors.TargetIsBase(target.Code));
        }

        var displayLocale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (displayLocale.IsFailure)
        {
            return Result.Failure<TranslationQueueDto>(displayLocale.Error);
        }

        return Result.Success(await store.GetQueueAsync(
            target.Code,
            state,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            request.Page,
            request.PageSize,
            displayLocale.Value,
            cancellationToken));
    }

    /// <summary>Null when the caller asked for a state that does not exist.</summary>
    private static string? NormaliseState(string? state) =>
        string.IsNullOrWhiteSpace(state)
            ? "attention"
            : state.Trim().ToLowerInvariant() switch
            {
                "attention" => "attention",
                "missing" => "missing",
                "outdated" => "outdated",
                "review" => "review",
                "all" => "all",
                _ => null,
            };
}
