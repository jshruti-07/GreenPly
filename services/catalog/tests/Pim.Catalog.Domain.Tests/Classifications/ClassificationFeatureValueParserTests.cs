using Pim.Catalog.Domain.Classifications;
using Pim.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Classifications;

public sealed class ClassificationFeatureValueParserTests
{
    private static Result<ParsedFeatureValue> Parse(string dataType, string? value) =>
        ClassificationFeatureValueParser.Parse("EF000008", dataType, value);

    [Theory]
    [InlineData(ClassificationDataType.Numeric)]
    [InlineData(ClassificationDataType.Alpha)]
    [InlineData(ClassificationDataType.Logical)]
    [InlineData(ClassificationDataType.Range)]
    public void BlankValue_ClearsTheFeature(string dataType)
    {
        var result = Parse(dataType, "   ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void UnknownDataType_IsRejected()
    {
        var result = Parse("colour", "red");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Classification.UnknownDataType");
    }

    [Fact]
    public void Alpha_IsTrimmed()
    {
        var result = Parse(ClassificationDataType.Alpha, "  Steel  ");

        result.Value.Text.ShouldBe("Steel");
    }

    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("2100.5", 2100.5)]
    [InlineData("-3", -3)]
    public void Numeric_Parses(string input, decimal expected)
    {
        Parse(ClassificationDataType.Numeric, input).Value.Number.ShouldBe(expected);
    }

    [Fact]
    public void Numeric_UsesInvariantCulture_NotTheServerLocale()
    {
        // '1,5' is one-and-a-half in German. Accepting it would make the stored
        // value depend on where the server runs, so it must be rejected.
        Parse(ClassificationDataType.Numeric, "1,5").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Numeric_RejectsText()
    {
        var result = Parse(ClassificationDataType.Numeric, "ninety");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Classification.InvalidValue");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("Yes")]
    [InlineData("1")]
    public void Logical_AcceptsTruthyForms(string input) =>
        Parse(ClassificationDataType.Logical, input).Value.Boolean.ShouldBe(true);

    [Theory]
    [InlineData("false")]
    [InlineData("No")]
    [InlineData("0")]
    public void Logical_AcceptsFalsyForms(string input) =>
        Parse(ClassificationDataType.Logical, input).Value.Boolean.ShouldBe(false);

    [Fact]
    public void Logical_RejectsAnythingElse() =>
        Parse(ClassificationDataType.Logical, "maybe").IsFailure.ShouldBeTrue();

    [Theory]
    [InlineData("10-20")]
    [InlineData("10.5 - 20.5")]
    [InlineData("5-5")]
    public void Range_AcceptsWellFormedPairs(string input) =>
        Parse(ClassificationDataType.Range, input).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("20-10")]     // high before low
    [InlineData("10")]        // not a pair
    [InlineData("10-20-30")]
    [InlineData("a-b")]
    public void Range_RejectsMalformedPairs(string input) =>
        Parse(ClassificationDataType.Range, input).IsFailure.ShouldBeTrue();

    [Fact]
    public void Range_IsStoredAsText_NotSplitIntoNumbers()
    {
        // The standard defines a range as a single value; splitting it would
        // not round-trip back to the exchange format.
        var result = Parse(ClassificationDataType.Range, "10-20");

        result.Value.Text.ShouldBe("10-20");
        result.Value.Number.ShouldBeNull();
    }

    [Fact]
    public void OverlongValue_IsRejected()
    {
        var result = Parse(
            ClassificationDataType.Alpha,
            new string('x', ClassificationFeatureValueParser.MaxTextLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Classification.ValueTooLong");
    }

    [Fact]
    public void Format_KeepsTrailingZerosOnWholeNumbers()
    {
        // Regression guard: an earlier reader stripped zeros from the whole
        // number and turned 1000 into 1 (caught in smoke, 2026-08-15).
        var parsed = Parse(ClassificationDataType.Numeric, "1000").Value;

        ClassificationFeatureValueParser.Format(parsed).ShouldBe("1000");
    }

    [Fact]
    public void Format_TrimsTrailingZerosOnlyInTheFraction()
    {
        var parsed = new ParsedFeatureValue(null, 20.50m, null);

        ClassificationFeatureValueParser.Format(parsed).ShouldBe("20.5");
    }

    [Fact]
    public void Format_RendersBooleansAsLowercaseLiterals()
    {
        ClassificationFeatureValueParser.Format(new ParsedFeatureValue(null, null, true)).ShouldBe("true");
        ClassificationFeatureValueParser.Format(new ParsedFeatureValue(null, null, false)).ShouldBe("false");
    }
}
