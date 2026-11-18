using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AttributeLibrary;

public sealed class GetAttributeLibraryQueryHandler(
    IAttributeLibraryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetAttributeLibraryQuery, Result<AttributeLibraryDto>>
{
    public async Task<Result<AttributeLibraryDto>> Handle(
        GetAttributeLibraryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Labels follow the reader. The library itself does not: an attribute
        // that exists in German exists in French, whether or not anybody has
        // written its French label yet — and the missing label is the thing
        // this screen is for finding.
        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);

        return locale.IsFailure
            ? Result.Failure<AttributeLibraryDto>(locale.Error)
            : Result.Success(await store.GetLibraryAsync(locale.Value, cancellationToken));
    }
}

public sealed class GetAttributeDetailQueryHandler(
    IAttributeLibraryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetAttributeDetailQuery, Result<AttributeDetailDto>>
{
    public async Task<Result<AttributeDetailDto>> Handle(
        GetAttributeDetailQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<AttributeDetailDto>(AttributeLibraryErrors.CodeRequired);
        }

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<AttributeDetailDto>(locale.Error);
        }

        var detail = await store.GetDetailAsync(request.Code, locale.Value, cancellationToken);

        return detail is null
            ? Result.Failure<AttributeDetailDto>(AttributeLibraryErrors.NotFound(request.Code))
            : Result.Success(detail);
    }
}
