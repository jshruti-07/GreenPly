using Pim.Catalog.Domain.Products;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Products;

public sealed class ProductVariantTests
{
    private static Product Create() =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, 1, "en", "Fire Door 90").Value;

    [Fact]
    public void NewProduct_HasNoVariants()
    {
        Create().Skus.ShouldBeEmpty();
    }

    [Fact]
    public void AddSku_AddsAnActiveVariantAndBumpsRowVersion()
    {
        var product = Create();

        var result = product.AddSku("FD-90-GS-1000-L", "4012345678901", "ERP-1", expectedRowVersion: 1);

        result.IsSuccess.ShouldBeTrue();
        var sku = product.Skus.ShouldHaveSingleItem();
        sku.Code.ShouldBe("FD-90-GS-1000-L");
        sku.Gtin.ShouldBe("4012345678901");
        sku.ErpMaterialNo.ShouldBe("ERP-1");
        sku.Status.ShouldBe(SkuStatus.Active);
        product.RowVersion.ShouldBe(2);
    }

    [Fact]
    public void AddSku_UppercasesAndTrimsTheCode()
    {
        var product = Create();

        product.AddSku("  fd-90-l  ", null, null, 1).IsSuccess.ShouldBeTrue();

        product.Skus.ShouldHaveSingleItem().Code.ShouldBe("FD-90-L");
    }

    [Fact]
    public void AddSku_AssignsIncreasingSortOrder()
    {
        var product = Create();

        product.AddSku("A-1", null, null, 1);
        product.AddSku("A-2", null, null, 2);

        product.Skus.Select(s => s.SortOrder).ShouldBe([1, 2]);
    }

    [Fact]
    public void AddSku_RejectsADuplicateCode()
    {
        var product = Create();
        product.AddSku("A-1", null, null, 1);

        var result = product.AddSku("a-1", null, null, 2);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.Code.Duplicate");
        product.Skus.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSku_RejectsAnEmptyCode(string code)
    {
        var result = Create().AddSku(code, null, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.Code.Empty");
    }

    [Theory]
    [InlineData("FD 90 L")]
    [InlineData("FD--90")]
    [InlineData("FD-90-")]
    [InlineData("FD_90")]
    public void AddSku_RejectsMalformedCodes(string code)
    {
        var result = Create().AddSku(code, null, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.Code.InvalidFormat");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    [InlineData("123456789012345")]
    public void AddSku_RejectsAnInvalidGtin(string gtin)
    {
        var result = Create().AddSku("A-1", gtin, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.Gtin.Invalid");
    }

    [Fact]
    public void AddSku_WithStaleRowVersion_ChangesNothing()
    {
        var product = Create();
        product.AddSku("A-1", null, null, 1);

        var result = product.AddSku("A-2", null, null, 1); // stale

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.ConcurrentModification");
        product.Skus.Count.ShouldBe(1);
    }

    [Fact]
    public void UpdateSku_ChangesCommercialFields()
    {
        var product = Create();
        product.AddSku("A-1", null, null, 1);

        var result = product.UpdateSku("A-1", "4012345678901", "ERP-9", SkuStatus.Blocked, 2);

        result.IsSuccess.ShouldBeTrue();
        var sku = product.Skus.ShouldHaveSingleItem();
        sku.Gtin.ShouldBe("4012345678901");
        sku.ErpMaterialNo.ShouldBe("ERP-9");
        sku.Status.ShouldBe(SkuStatus.Blocked);
        product.RowVersion.ShouldBe(3);
    }

    [Fact]
    public void UpdateSku_RejectsAnInvalidGtinAndLeavesTheSkuUntouched()
    {
        var product = Create();
        product.AddSku("A-1", "4012345678901", null, 1);

        var result = product.UpdateSku("A-1", "nope", null, SkuStatus.Active, 2);

        result.IsFailure.ShouldBeTrue();
        product.Skus.ShouldHaveSingleItem().Gtin.ShouldBe("4012345678901");
    }

    [Fact]
    public void UpdateSku_UnknownCode_Fails()
    {
        var product = Create();

        var result = product.UpdateSku("MISSING", null, null, SkuStatus.Active, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.NotFound");
    }

    [Fact]
    public void RemoveSku_RemovesIt()
    {
        var product = Create();
        product.AddSku("A-1", null, null, 1);
        product.AddSku("A-2", null, null, 2);

        product.RemoveSku("A-1", 3).IsSuccess.ShouldBeTrue();

        product.Skus.ShouldHaveSingleItem().Code.ShouldBe("A-2");
    }

    [Fact]
    public void RemoveSku_UnknownCode_Fails()
    {
        var result = Create().RemoveSku("MISSING", 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sku.NotFound");
    }

    [Fact]
    public void RemoveSku_WithStaleRowVersion_ChangesNothing()
    {
        var product = Create();
        product.AddSku("A-1", null, null, 1);

        var result = product.RemoveSku("A-1", 1); // stale

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.ConcurrentModification");
        product.Skus.Count.ShouldBe(1);
    }

    [Fact]
    public void SkuStatus_RoundTripsThroughItsDatabaseForm()
    {
        foreach (var status in Enum.GetValues<SkuStatus>())
        {
            SkuStatusMap.FromDb(SkuStatusMap.ToDb(status)).ShouldBe(status);
        }

        SkuStatusMap.ToDb(SkuStatus.EndOfLife).ShouldBe("eol");
    }
}
