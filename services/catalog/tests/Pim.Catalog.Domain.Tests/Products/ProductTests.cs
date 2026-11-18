using Pim.Catalog.Domain.Products;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Products;

public sealed class ProductTests
{
    private static Product CreateValidProduct() =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, primaryNodeId: 1, baseLocale: "en",
            name: "Fire Door 90").Value;

    [Fact]
    public void Create_WithValidInput_ReturnsActiveDraftProduct()
    {
        var result = Product.Create(
            ProductCode.Create("FD-90-GS-1000").Value,
            primaryNodeId: 1,
            baseLocale: "en",
            name: "Fire Door 90",
            gtin: "12345678",
            sapMaterialNo: "MAT-001");

        result.IsSuccess.ShouldBeTrue();
        var product = result.Value;
        product.LifecycleState.ShouldBe(LifecycleState.Active);
        product.WorkflowState.ShouldBe(WorkflowState.Draft);
        product.PublicId.ShouldNotBe(Guid.Empty);
        product.RowVersion.ShouldBe(1);
        product.Gtin.ShouldBe("12345678");
    }

    [Fact]
    public void Create_RaisesProductCreatedDomainEvent()
    {
        var product = CreateValidProduct();

        var domainEvent = product.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ProductCreatedDomainEvent>();
        domainEvent.ProductCode.ShouldBe("FD-90-GS-1000");
        domainEvent.ProductPublicId.ShouldBe(product.PublicId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithInvalidPrimaryNode_Fails(long primaryNodeId)
    {
        var result = Product.Create(ProductCode.Create("FD-90").Value, primaryNodeId, "en", "Fire Door 90");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.PrimaryNode.Required");
    }

    [Fact]
    public void Create_WithMissingBaseLocale_Fails()
    {
        var result = Product.Create(ProductCode.Create("FD-90").Value, 1, "  ", "Fire Door 90");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.BaseLocale.Required");
    }

    [Fact]
    public void ChangeLifecycleState_ActiveToPhaseOut_Succeeds()
    {
        var product = CreateValidProduct();

        var result = product.ChangeLifecycleState(LifecycleState.PhaseOut);

        result.IsSuccess.ShouldBeTrue();
        product.LifecycleState.ShouldBe(LifecycleState.PhaseOut);
    }

    [Fact]
    public void ChangeLifecycleState_FollowsFullChain()
    {
        var product = CreateValidProduct();

        product.ChangeLifecycleState(LifecycleState.PhaseOut).IsSuccess.ShouldBeTrue();
        product.ChangeLifecycleState(LifecycleState.Discontinued).IsSuccess.ShouldBeTrue();
        product.LifecycleState.ShouldBe(LifecycleState.Discontinued);
    }

    [Theory]
    [InlineData(LifecycleState.Discontinued)]
    [InlineData(LifecycleState.Planned)]
    public void ChangeLifecycleState_SkippingStates_Fails(LifecycleState target)
    {
        var product = CreateValidProduct();

        var result = product.ChangeLifecycleState(target);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Lifecycle.InvalidTransition");
        product.LifecycleState.ShouldBe(LifecycleState.Active);
    }

    [Fact]
    public void ChangeLifecycleState_ToSameState_IsIdempotentAndRaisesNoEvent()
    {
        var product = CreateValidProduct();
        product.ClearDomainEvents();

        var result = product.ChangeLifecycleState(LifecycleState.Active);

        result.IsSuccess.ShouldBeTrue();
        product.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ChangeLifecycleState_RaisesLifecycleChangedEvent()
    {
        var product = CreateValidProduct();
        product.ClearDomainEvents();

        product.ChangeLifecycleState(LifecycleState.PhaseOut);

        var domainEvent = product.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ProductLifecycleChangedDomainEvent>();
        domainEvent.PreviousState.ShouldBe(LifecycleState.Active);
        domainEvent.NewState.ShouldBe(LifecycleState.PhaseOut);
    }
}
