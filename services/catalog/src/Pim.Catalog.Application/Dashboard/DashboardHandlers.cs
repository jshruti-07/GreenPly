using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Dashboard;

public sealed class GetProductCockpitQueryHandler(
    IDashboardStore store, DisplayLocaleResolver localeResolver)
    : IRequestHandler<GetProductCockpitQuery, Result<ProductCockpitDto>>
{
    public async Task<Result<ProductCockpitDto>> Handle(
        GetProductCockpitQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The cockpit renders attribute-group names and locale names, both of
        // which are labels, so it follows the reader like every other screen
        // (ADR-009). The figures underneath do not move — a catalogue does not
        // become more complete because someone switched to French.
        var locale = await localeResolver.ResolveAsync(request.Locale, cancellationToken);

        return locale.IsFailure
            ? Result.Failure<ProductCockpitDto>(locale.Error)
            : Result.Success(await store.GetAsync(locale.Value, cancellationToken));
    }
}
