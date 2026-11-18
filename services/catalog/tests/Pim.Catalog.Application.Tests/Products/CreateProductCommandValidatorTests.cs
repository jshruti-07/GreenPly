using Pim.Catalog.Application.Products.CreateProduct;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    private static CreateProductCommand Command(
        string code = "FD-90",
        long primaryNodeId = 1,
        string baseLocale = "en",
        string? gtin = null,
        string name = "Fire Door 90",
        string? owner = "Anna Becker") =>
        new(code, primaryNodeId, baseLocale, name, null, null, gtin, null, owner);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyName_Fails(string name)
    {
        _validator.Validate(Command(name: name)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void NameOverMaxLength_Fails()
    {
        var tooLong = new string('x', Pim.Catalog.Domain.Products.ProductTranslation.MaxNameLength + 1);
        _validator.Validate(Command(name: tooLong)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void MissingOwner_IsAllowed()
    {
        // Owner is optional until an identity service exists (decision 2026-08-14).
        _validator.Validate(Command(owner: null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ValidCommand_Passes()
    {
        _validator.Validate(Command()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyCode_Fails(string code)
    {
        _validator.Validate(Command(code: code)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void NonPositivePrimaryNode_Fails()
    {
        _validator.Validate(Command(primaryNodeId: 0)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void ValidLocales_Pass(string locale)
    {
        _validator.Validate(Command(baseLocale: locale)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("english")]
    [InlineData("EN")]
    [InlineData("en_GB")]
    public void InvalidLocales_Fail(string locale)
    {
        _validator.Validate(Command(baseLocale: locale)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("12345678901234")]
    public void ValidGtin_Passes(string gtin)
    {
        _validator.Validate(Command(gtin: gtin)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789012345")]
    [InlineData("ABCDEFGH")]
    public void InvalidGtin_Fails(string gtin)
    {
        _validator.Validate(Command(gtin: gtin)).IsValid.ShouldBeFalse();
    }
}
