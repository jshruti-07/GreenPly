using MediatR;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Workflow;

/// <summary>Where one product stands, where it can go, and what has happened to it.</summary>
public sealed record GetProductWorkflowQuery(string Code) : IRequest<Result<ProductWorkflowDto>>;

public sealed class GetProductWorkflowQueryHandler(WorkflowPresenter presenter)
    : IRequestHandler<GetProductWorkflowQuery, Result<ProductWorkflowDto>>
{
    public Task<Result<ProductWorkflowDto>> Handle(
        GetProductWorkflowQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return presenter.BuildAsync(request.Code, cancellationToken);
    }
}

/// <summary>
/// The pipeline itself. Exists so the front end can stop carrying its own copy
/// of the transition table: up to Sprint 26 the React app decided which buttons
/// to show from a hardcoded Record&lt;string, string[]&gt;, which meant
/// reconfiguring the process in the database changed what the server allowed
/// and not what the user was offered.
/// </summary>
public sealed record GetWorkflowDefinitionQuery(string EntityType = WorkflowTransitionService.ProductEntityType)
    : IRequest<Result<WorkflowDefinitionDto>>;

public sealed class GetWorkflowDefinitionQueryHandler(WorkflowPresenter presenter)
    : IRequestHandler<GetWorkflowDefinitionQuery, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        GetWorkflowDefinitionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dto = await presenter.BuildDefinitionAsync(request.EntityType, cancellationToken);

        return Result.Success(dto);
    }
}
