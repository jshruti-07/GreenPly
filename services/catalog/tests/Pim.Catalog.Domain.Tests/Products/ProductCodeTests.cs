using Pim.Catalog.Domain.Products;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Products;

public sealed class ProductCodeTests
{
    [Theory]
    [InlineData("FD-90-GS-1000")]
    [InlineData("ABC")]
    [InlineData("A1-B2")]
    public void Create_WithValidCode_Succeeds(string input)
    {
        var result = ProductCode.Create(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(input);
    }

    [Fact]
    public void Create_NormalizesCaseAndWhitespace()
    {
        var result = ProductCode.Create("  fd-90-gs-1000  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("FD-90-GS-1000");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyCode_FailsWithCodeEmpty(string? input)
    {
        var result = ProductCode.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ProductErrors.CodeEmpty);
    }

    [Theory]
    [InlineData("FD_90")]
    [InlineData("FD--90")]
    [InlineData("-FD90")]
    [InlineData("FD90-")]
    [InlineData("FD 90")]
    public void Create_WithInvalidFormat_FailsWithInvalidFormat(string input)
    {
        var result = ProductCode.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ProductErrors.CodeInvalidFormat);
    }

    [Fact]
    public void Create_ExceedingMaxLength_FailsWithTooLong()
    {
        var result = ProductCode.Create(new string('A', ProductCode.MaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ProductErrors.CodeTooLong);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var first = ProductCode.Create("FD-90").Value;
        var second = ProductCode.Create("fd-90").Value;

        first.ShouldBe(second);
    }
}
