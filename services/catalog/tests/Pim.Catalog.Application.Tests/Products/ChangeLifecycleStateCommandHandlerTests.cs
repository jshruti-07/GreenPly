using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pim.Catalog.Application.Products.ChangeLifecycleState;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class ChangeLifecycleStateCommandHandlerTests
{
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ChangeLifecycleStateCommandHandler _handler;

    public ChangeLifecycleStateCommandHandlerTests()
    {
        _handler = new ChangeLifecycleStateCommandHandler(
            _repository, _unitOfWork, NullLogger<ChangeLifecycleStateCommandHandler>.Instance);
    }

    private static Product ExistingProduct() =>
        Product.Create(ProductCode.Create("FD-90").Value, 1, "en", "Fire Door 90").Value;

    [Fact]
    public async Task Handle_ValidTransition_PersistsAndReturnsDto()
    {
        _repository.GetByCodeAsync(Arg.Any<ProductCode>(), Arg.Any<CancellationToken>())
            .Returns(ExistingProduct());

        var result = await _handler.Handle(
            new ChangeLifecycleStateCommand("FD-90", "PhaseOut"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LifecycleState.ShouldBe("PhaseOut");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownProduct_ReturnsNotFound()
    {
        _repository.GetByCodeAsync(Arg.Any<ProductCode>(), Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var result = await _handler.Handle(
            new ChangeLifecycleStateCommand("FD-90", "PhaseOut"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.NotFound");
    }

    [Fact]
    public async Task Handle_UnknownState_ReturnsValidationError()
    {
        var result = await _handler.Handle(
            new ChangeLifecycleStateCommand("FD-90", "Retired"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Lifecycle.UnknownState");
    }

    [Fact]
    public async Task Handle_InvalidTransition_ReturnsConflictAndDoesNotPersist()
    {
        _repository.GetByCodeAsync(Arg.Any<ProductCode>(), Arg.Any<CancellationToken>())
            .Returns(ExistingProduct());

        var result = await _handler.Handle(
            new ChangeLifecycleStateCommand("FD-90", "Discontinued"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Lifecycle.InvalidTransition");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
