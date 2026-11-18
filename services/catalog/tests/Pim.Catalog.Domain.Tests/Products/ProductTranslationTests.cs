using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Products;

public sealed class ProductTranslationTests
{
    private static Result<Product> Create(string name = "Fire Door 90", string baseLocale = "en") =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, 1, baseLocale, name);

    [Fact]
    public void Create_StoresNameInBaseLocale()
    {
        var product = Create().Value;

        product.BaseLocaleName.ShouldBe("Fire Door 90");
        var translation = product.Translations.ShouldHaveSingleItem();
        translation.LocaleCode.ShouldBe("en");
        translation.TranslationStatus.ShouldBe("human");
    }

    [Fact]
    public void Create_TrimsName()
    {
        Create(name: "  Fire Door 90  ").Value.BaseLocaleName.ShouldBe("Fire Door 90");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Fails(string name)
    {
        var result = Create(name: name);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Name.Required");
    }

    [Fact]
    public void Create_WithOverlongName_Fails()
    {
        var result = Create(name: new string('x', ProductTranslation.MaxNameLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Name.TooLong");
    }

    [Fact]
    public void SetName_AddsTranslationForNewLocale()
    {
        var product = Create().Value;

        product.SetName("de-DE", "Brandschutztür 90").IsSuccess.ShouldBeTrue();

        product.Translations.Count.ShouldBe(2);
        product.BaseLocaleName.ShouldBe("Fire Door 90"); // base locale untouched
    }

    [Fact]
    public void SetName_UpdatesExistingLocaleInPlace()
    {
        var product = Create().Value;

        product.SetName("en", "Fire Door 90 Renamed").IsSuccess.ShouldBeTrue();

        product.Translations.ShouldHaveSingleItem().Name.ShouldBe("Fire Door 90 Renamed");
        product.BaseLocaleName.ShouldBe("Fire Door 90 Renamed");
    }

    [Fact]
    public void SetName_WithEmptyName_Fails()
    {
        var product = Create().Value;

        var result = product.SetName("en", "  ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Name.Required");
        product.BaseLocaleName.ShouldBe("Fire Door 90"); // unchanged
    }

    [Fact]
    public void Create_StoresOwnerDisplayName()
    {
        var product = Product.Create(
            ProductCode.Create("FD-90").Value, 1, "en", "Fire Door 90",
            ownerDisplayName: "  Anna Becker  ").Value;

        product.OwnerDisplayName.ShouldBe("Anna Becker");
    }

    [Fact]
    public void Create_WithoutOwner_LeavesOwnerNull()
    {
        Create().Value.OwnerDisplayName.ShouldBeNull();
    }
}
