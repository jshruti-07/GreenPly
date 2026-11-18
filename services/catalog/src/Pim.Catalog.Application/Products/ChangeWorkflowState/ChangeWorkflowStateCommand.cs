using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Workflow;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ChangeWorkflowState;

/// <summary>Advances or rejects a product in the editorial workflow.</summary>
public sealed record ChangeWorkflowStateCommand(string Code, string TargetState, int RowVersion)
    : IRequest<Result<ProductDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    // Publishing is the transition people look for in a history, so it gets
    // its own verb rather than being flattened into a generic "updated".
    string IAuditableCommand.AuditAction =>
        string.Equals(TargetState, "Published", StringComparison.OrdinalIgnoreCase)
            ? "published"
            : "workflow";

    string IAuditableCommand.AuditDetail => $"Moved to {TargetState}";
}

public sealed class ChangeWorkflowStateCommandValidator : AbstractValidator<ChangeWorkflowStateCommand>
{
    public ChangeWorkflowStateCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();

        RuleFor(x => x.TargetState)
            .Must(s => Enum.TryParse<WorkflowState>(s, ignoreCase: true, out _))
            .WithMessage(x =>
                $"Unknown workflow state '{x.TargetState}'. Allowed: {string.Join(", ", Enum.GetNames<WorkflowState>())}.");

        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

/// <summary>
/// The state-oriented entry point, kept because the detail header has posted
/// target states since Sprint 4 and because a bulk "move these to Review"
/// naturally speaks in destinations.
///
/// It no longer owns any rules. Sprint 27 gave both this and the action-oriented
/// command one shared path through WorkflowTransitionService, so a product moved
/// from the header and a product moved from the Workflow tab open the same task,
/// write the same history and cannot diverge.
/// </summary>
public sealed partial class ChangeWorkflowStateCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    WorkflowTransitionService workflow,
    ILogger<ChangeWorkflowStateCommandHandler> logger)
    : IRequestHandler<ChangeWorkflowStateCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        ChangeWorkflowStateCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductDto>(codeResult.Error);
        }

        var product = await productRepository.GetByCodeAsync(codeResult.Value, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductDto>(ProductErrors.NotFound(codeResult.Value.Value));
        }

        if (product.RowVersion != request.RowVersion)
        {
            return Result.Failure<ProductDto>(ProductErrors.ConcurrentModification);
        }

        // Safe: the validator has already rejected anything that will not parse.
        var target = Enum.Parse<WorkflowState>(request.TargetState, ignoreCase: true);

        var moved = await workflow.PerformToStateAsync(
            product, target, comment: null, actorDisplay: null, cancellationToken);

        if (moved.IsFailure)
        {
            return Result.Failure<ProductDto>(moved.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.WorkflowChanged(logger, product.Code.Value, target);

        return ProductDto.FromDomain(product);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Product {ProductCode} workflow changed to {State}")]
        public static partial void WorkflowChanged(ILogger logger, string productCode, WorkflowState state);
    }
}
