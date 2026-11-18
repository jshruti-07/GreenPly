using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pim.Catalog.Application.Products.CreateProduct;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class CreateProductCommandHandlerTests
{
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(
            _repository, _unitOfWork, NullLogger<CreateProductCommandHandler>.Instance);
    }

    private static CreateProductCommand ValidCommand() => new(
        Code: "FD-90-GS-1000",
        PrimaryNodeId: 1,
        BaseLocale: "en",
        Name: "Fire Door 90 Min",
        BrandId: null,
        AttributeSetId: null,
        Gtin: null,
        SapMaterialNo: null,
        OwnerDisplayName: "Anna Becker");

    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        _repository.ExistsByCodeAsync(Arg.Any<ProductCode>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("FD-90-GS-1000");
        result.Value.LifecycleState.ShouldBe("Active");
        _repository.Received(1).Add(Arg.Is<Product>(p => p.Code.Value == "FD-90-GS-1000"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDuplicateCode_ReturnsConflictAndDoesNotPersist()
    {
        _repository.ExistsByCodeAsync(Arg.Any<ProductCode>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ProductErrors.DuplicateCode);
        _repository.DidNotReceive().Add(Arg.Any<Product>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMalformedCode_ReturnsValidationFailure()
    {
        var command = ValidCommand() with { Code = "bad code!" };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ProductErrors.CodeInvalidFormat);
        _repository.DidNotReceive().Add(Arg.Any<Product>());
    }
}
