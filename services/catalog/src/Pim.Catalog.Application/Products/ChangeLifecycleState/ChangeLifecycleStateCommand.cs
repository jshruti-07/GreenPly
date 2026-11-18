using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ChangeLifecycleState;

public sealed record ChangeLifecycleStateCommand(string Code, string TargetState)
    : IRequest<Result<ProductDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "lifecycle";

    string IAuditableCommand.AuditDetail => $"Lifecycle set to {TargetState}";
}
