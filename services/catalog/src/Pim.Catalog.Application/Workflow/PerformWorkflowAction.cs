using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Workflow;

/// <summary>
/// Performs a named workflow action on a product — the verb, not the
/// destination. Where the verb lands is the pipeline's business.
/// </summary>
/// <param name="Actor">
/// Who is claiming to do this. UNVERIFIED: there is no authentication (R-3), so
/// this is a string the caller sent, and the approval trail records it as such
/// (R-26). It exists now rather than later so the trail has the right SHAPE
/// when identity arrives, with nothing false to back-fill over.
/// </param>
public sealed record PerformWorkflowActionCommand(
    string Code,
    string Action,
    string? Comment,
    string? Actor,
    int RowVersion)
    : IRequest<Result<ProductWorkflowDto>>, IAuditableCommand
{
    /// <summary>
    /// Where the action actually landed, filled in by the handler before the
    /// audit behaviour reads the two properties below.
    ///
    /// This is a mutable field on a command and that deserves a defence.
    /// AuditBehavior runs the handler FIRST and reads AuditAction afterwards,
    /// by design, so it can skip failed commands. An action-oriented command
    /// cannot know its own destination in advance: APPROVE publishes a product
    /// standing in Approval and merely passes review for one standing in
    /// Review. Without this, every workflow move would be logged under one flat
    /// verb and "when was this published" — the single question people bring to
    /// an activity log — would have no answer in it.
    /// </summary>
    internal string? ResultingStepCode { get; set; }

    internal string? TransitionSummary { get; set; }

    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction =>
        string.Equals(ResultingStepCode, "published", StringComparison.OrdinalIgnoreCase)
            ? "published"
            : "workflow";

    string IAuditableCommand.AuditDetail
    {
        get
        {
            var summary = string.IsNullOrWhiteSpace(TransitionSummary)
                ? $"Action {Action}"
                : TransitionSummary;

            return string.IsNullOrWhiteSpace(Comment) ? summary : $"{summary} — {Comment.Trim()}";
        }
    }
}

public sealed class PerformWorkflowActionCommandValidator : AbstractValidator<PerformWorkflowActionCommand>
{
    public PerformWorkflowActionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();

        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => WorkflowActions.TryParse(a, out _))
            .WithMessage(x =>
                $"'{x.Action}' is not a workflow action. Accepted: "
                + $"{string.Join(", ", WorkflowActions.All.Select(WorkflowActions.ToToken))}.");

        // Long enough for a real reviewer note, bounded so the column does not
        // become somewhere to paste a specification.
        RuleFor(x => x.Comment).MaximumLength(2000);
        RuleFor(x => x.Actor).MaximumLength(200);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

public sealed class PerformWorkflowActionCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    WorkflowTransitionService workflow,
    WorkflowPresenter presenter)
    : IRequestHandler<PerformWorkflowActionCommand, Result<ProductWorkflowDto>>
{
    public async Task<Result<ProductWorkflowDto>> Handle(
        PerformWorkflowActionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductWorkflowDto>(codeResult.Error);
        }

        var product = await productRepository.GetByCodeAsync(codeResult.Value, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductWorkflowDto>(ProductErrors.NotFound(codeResult.Value.Value));
        }

        if (product.RowVersion != request.RowVersion)
        {
            return Result.Failure<ProductWorkflowDto>(ProductErrors.ConcurrentModification);
        }

        var definition = await workflow.GetProductPipelineAsync(cancellationToken);
        var origin = definition.StepFor(product.WorkflowState);

        var moved = await workflow.PerformAsync(
            product, request.Action, request.Comment, request.Actor, cancellationToken);

        if (moved.IsFailure)
        {
            return Result.Failure<ProductWorkflowDto>(moved.Error);
        }

        // One save: the approval task, its new history row, and the projection
        // on product.workflow_state. This is the reason approval_task is mapped
        // with EF Core instead of written through a Dapper store.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        request.ResultingStepCode = moved.Value.Code;
        request.TransitionSummary = $"{origin?.Name ?? "Unknown"} to {moved.Value.Name}";

        return await presenter.BuildAsync(product.Code.Value, cancellationToken);
    }
}
