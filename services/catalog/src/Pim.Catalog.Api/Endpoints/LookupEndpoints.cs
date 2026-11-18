using MediatR;
using Pim.Catalog.Application.Lookups;

namespace Pim.Api.Endpoints;

/// <summary>
/// Reference-data lookups backing the create wizard's selects. Read-only and
/// small; no paging until a catalogue outgrows a single select.
/// </summary>
public static class LookupEndpoints
{
    public static void MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Lookups");

        group.MapGet("/brands", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBrandsQuery(), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<IReadOnlyList<BrandLookupDto>>()
        .WithSummary("Active brands for the create wizard's Brand select.");

        group.MapGet("/taxonomy-nodes", async (
            ISender sender,
            CancellationToken ct,
            string? nodeType = null) =>
        {
            var result = await sender.Send(new GetTaxonomyNodesQuery(nodeType), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<IReadOnlyList<TaxonomyNodeLookupDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Active taxonomy nodes, optionally filtered by nodeType (FAMILY, CATEGORY, ...).");

        // Backs the language selector. Separate from the wizard lookups above
        // because the selector is part of the shell and loads before any
        // product screen does.
        group.MapGet("/locales", async (ILocaleCatalog locales, CancellationToken ct) =>
        {
            var all = await locales.GetAllAsync(ct);
            return Results.Ok(all);
        })
        .Produces<IReadOnlyList<LocaleDto>>()
        .WithSummary("Locales this catalogue is configured for, base locale first.");
    }
}
