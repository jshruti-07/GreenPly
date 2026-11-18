using MediatR;
using Pim.Catalog.Application.Dashboard;

namespace Pim.Api.Endpoints;

/// <summary>
/// The Product Cockpit.
///
/// One endpoint, not six. Every figure on the screen comes from one pass so the
/// panels cannot disagree with each other: a dashboard assembled from six
/// independent requests can show an average completeness computed before an
/// edit next to a quality distribution computed after it, and the reader has no
/// way to know. One request, one point in time.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Dashboard");

        group.MapGet("/dashboard", async (
            string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductCockpitQuery(locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductCockpitDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary(
            "Product cockpit: catalogue KPIs, quality distribution, completeness by " +
            "attribute group, workflow pipeline, locale coverage and recent activity. " +
            "Panels v4.2 specifies but this build cannot honestly populate are returned " +
            "in omittedPanels with the reason, rather than omitted silently.");
    }
}
