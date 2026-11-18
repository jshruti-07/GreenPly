using MediatR;
using Microsoft.Extensions.Logging;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.ChangeLifecycleState;

public sealed partial class ChangeLifecycleStateCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    ILogger<ChangeLifecycleStateCommandHandler> logger)
    : IRequestHandler<ChangeLifecycleStateCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(ChangeLifecycleStateCommand request, CancellationToken cancellationToken)
    {
        var codeResult = ProductCode.Create(request.Code);
        if (codeResult.IsFailure)
        {
            return Result.Failure<ProductDto>(codeResult.Error);
        }

        if (!Enum.TryParse<LifecycleState>(request.TargetState, ignoreCase: true, out var targetState))
        {
            return Result.Failure<ProductDto>(Error.Validation(
                "Product.Lifecycle.UnknownState",
                $"Unknown lifecycle state '{request.TargetState}'."));
        }

        var product = await productRepository.GetByCodeAsync(codeResult.Value, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductDto>(ProductErrors.NotFound(codeResult.Value.Value));
        }

        var transition = product.ChangeLifecycleState(targetState);
        if (transition.IsFailure)
        {
            return Result.Failure<ProductDto>(transition.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.LifecycleChanged(logger, product.Code.Value, targetState);

        return ProductDto.FromDomain(product);
    }

    /// <summary>Source-generated logging (CA1848/CA1873-compliant).</summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Product {ProductCode} lifecycle changed to {State}")]
        public static partial void LifecycleChanged(ILogger logger, string productCode, LifecycleState state);
    }
}
