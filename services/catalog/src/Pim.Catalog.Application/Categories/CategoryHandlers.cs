using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Categories;

public sealed class GetCategoryTreeQueryHandler(
    ICategoryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetCategoryTreeQuery, Result<IReadOnlyList<CategoryTreeNodeDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryTreeNodeDto>>> Handle(
        GetCategoryTreeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Labels follow the reader; the tree's SHAPE and its counts do not.
        // Rendering a French tree with fewer nodes because some are untranslated
        // would be the single worst thing this screen could do — the taxonomy is
        // the same taxonomy in every language.
        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);

        return locale.IsFailure
            ? Result.Failure<IReadOnlyList<CategoryTreeNodeDto>>(locale.Error)
            : Result.Success(await store.GetTreeAsync(locale.Value, cancellationToken));
    }
}

public sealed class GetCategoryDetailQueryHandler(
    ICategoryStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetCategoryDetailQuery, Result<CategoryDetailDto>>
{
    public async Task<Result<CategoryDetailDto>> Handle(
        GetCategoryDetailQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<CategoryDetailDto>(CategoryErrors.CodeRequired);
        }

        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);
        if (locale.IsFailure)
        {
            return Result.Failure<CategoryDetailDto>(locale.Error);
        }

        var detail = await store.GetDetailAsync(request.Code, locale.Value, cancellationToken);

        // 404 rather than an empty panel. An unknown code is a broken link or a
        // deleted node, and a blank right-hand side reads as "this category has
        // nothing in it" — which is a statement about the catalogue, not about
        // the request.
        return detail is null
            ? Result.Failure<CategoryDetailDto>(CategoryErrors.NotFound(request.Code))
            : Result.Success(detail);
    }
}
