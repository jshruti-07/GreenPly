using MediatR;
using Pim.Catalog.Application.Workflow;

namespace Pim.Api.Endpoints;

/// <summary>
/// Workflow endpoints.
///
/// Product-scoped reads and actions live under /products/{code}/workflow
/// because that is where the client asks the question. The pipeline itself is
/// under /workflow/definition because it belongs to no product — and it exists
/// so the front end can stop carrying a hardcoded copy of the transition table.
///
/// NO AUTHENTICATION, like every other endpoint in this build (R-3). That is
/// materially worse here than elsewhere: these routes approve and publish, and
/// they record an actor the caller supplies about themselves. Local development
/// only, and R-26 says so in the risk register rather than in a comment nobody
/// reads.
/// </summary>
public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var products = app.MapGroup("/api/v1/products").WithTags("Workflow");

        products.MapGet("/{code}/workflow", async (
            string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductWorkflowQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductWorkflowDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Where the product stands in the editorial pipeline, which actions are "
                   + "available from there, and the approval trail. Actor attribution is "
                   + "unverified until authentication ships.");

        products.MapPost("/{code}/workflow/actions", async (
            string code,
            WorkflowActionRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var result = await sender.Send(
                new PerformWorkflowActionCommand(
                    code, request.Action, request.Comment, request.Actor, request.RowVersion),
                ct);

            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductWorkflowDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Performs a workflow action (SUBMIT, APPROVE, REJECT, REQUEST_CHANGES) and "
                   + "returns the product's new workflow document. Responds 409 if the action is "
                   + "not available from the current step or the product changed underneath.");

        var workflow = app.MapGroup("/api/v1/workflow").WithTags("Workflow");

        workflow.MapGet("/board", async (
            ISender sender, CancellationToken ct, int cardsPerColumn = 12, string? locale = null) =>
        {
            var result = await sender.Send(new GetWorkflowBoardQuery(cardsPerColumn, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<WorkflowBoardDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("The editorial pipeline across the whole catalogue, one column per configured step. "
                   + "Each column carries its TRUE total and its most recently touched cards — the two "
                   + "differ, and the payload says so. States the pipeline does not define appear as "
                   + "their own column rather than being dropped.");

        workflow.MapGet("/queue", async (
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? step = null,
            string? assignee = null,
            string? priority = null,
            string? search = null,
            string sortBy = GetWorkflowQueueQuery.DefaultSortBy,
            string sortDir = GetWorkflowQueueQuery.DefaultSortDir,
            string? locale = null) =>
        {
            var result = await sender.Send(
                new GetWorkflowQueueQuery(page, pageSize, step, assignee, priority, search, sortBy, sortDir, locale),
                ct);

            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<WorkflowQueueDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("The same pipeline as a filterable, paged list — and \"my queue\" when filtered by "
                   + "assignee. Lists PRODUCTS in the pipeline, not approval tasks: most of the catalogue "
                   + "has never been through it, and a list built from tasks would show a handful of rows "
                   + "while claiming to cover the catalogue. An unknown step or priority is rejected rather "
                   + "than answered with an empty list.");

        workflow.MapGet("/definition", async (
            ISender sender, CancellationToken ct, string entityType = "PRODUCT") =>
        {
            var result = await sender.Send(new GetWorkflowDefinitionQuery(entityType), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<WorkflowDefinitionDto>()
        .WithSummary("The configured pipeline: steps in order and the transitions between them. "
                   + "Configuration, not code — reconfiguring it changes what the UI offers.");
    }

    /// <param name="Actor">
    /// Unverified. Recorded verbatim in the approval trail because there is no
    /// identity service to check it against (R-26).
    /// </param>
    public sealed record WorkflowActionRequest(
        string Action, string? Comment, string? Actor, int RowVersion);
}
