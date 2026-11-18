using MediatR;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Compliance;

public sealed class GetProductComplianceQueryHandler(IProductComplianceStore store)
    : IRequestHandler<GetProductComplianceQuery, Result<ProductComplianceDto>>
{
    public async Task<Result<ProductComplianceDto>> Handle(
        GetProductComplianceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await store.GetAsync(request.Code, cancellationToken);

        return result is null
            ? Result.Failure<ProductComplianceDto>(ProductErrors.NotFound(request.Code))
            : Result.Success(result);
    }
}

public sealed class AddComplianceDocumentCommandHandler(IProductComplianceStore store)
    : IRequestHandler<AddComplianceDocumentCommand, Result<ProductComplianceDto>>
{
    public Task<Result<ProductComplianceDto>> Handle(
        AddComplianceDocumentCommand request,
        CancellationToken cancellationToken) =>
        store.AddAsync(request, cancellationToken);
}

public sealed class UpdateComplianceDocumentCommandHandler(IProductComplianceStore store)
    : IRequestHandler<UpdateComplianceDocumentCommand, Result<ProductComplianceDto>>
{
    public Task<Result<ProductComplianceDto>> Handle(
        UpdateComplianceDocumentCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(request, cancellationToken);
}

public sealed class RemoveComplianceDocumentCommandHandler(IProductComplianceStore store)
    : IRequestHandler<RemoveComplianceDocumentCommand, Result<ProductComplianceDto>>
{
    public Task<Result<ProductComplianceDto>> Handle(
        RemoveComplianceDocumentCommand request,
        CancellationToken cancellationToken) =>
        store.RemoveAsync(request, cancellationToken);
}
