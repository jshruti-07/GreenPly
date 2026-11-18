using Pim.SharedKernel.Results;
using Pim.Catalog.Domain.Attributes;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Attributes;

public sealed class AttributeValueParserTests
{
    private static readonly string[] FireRatings = ["ei30", "ei60", "ei90"];

    private static Result<ParsedAttributeValue> Parse(string dataType, string? value, string[]? options = null) =>
        AttributeValueParser.Parse("attr", dataType, value, options ?? []);

    [Theory]
    [InlineData(AttributeDataType.Text)]
    [InlineData(AttributeDataType.Number)]
    [InlineData(AttributeDataType.TrueFalse)]
    [InlineData(AttributeDataType.CalendarDate)]
    public void EmptyValue_ClearsTheAttribute(string dataType)
    {
        var result = Parse(dataType, "   ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Text_IsTrimmed()
    {
        Parse(AttributeDataType.Text, "  steel  ").Value.Text.ShouldBe("steel");
    }

    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("-5", -5)]
    public void Number_AcceptsIntegers(string raw, int expected)
    {
        Parse(AttributeDataType.Number, raw).Value.Number.ShouldBe(expected);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("12px")]
    public void Number_RejectsNonIntegers(string raw)
    {
        var result = Parse(AttributeDataType.Number, raw);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Attribute.InvalidValue");
    }

    [Fact]
    public void Decimal_UsesInvariantCulture()
    {
        // "1.5" must be one-and-a-half regardless of server locale.
        Parse(AttributeDataType.DecimalNumber, "1.5").Value.Number.ShouldBe(1.5m);

        // A German decimal comma is rejected rather than silently reinterpreted
        // as 15 — the wire format is canonical, the UI formats for display.
        var commaResult = Parse(AttributeDataType.DecimalNumber, "1,5");
        commaResult.IsFailure.ShouldBeTrue();
        commaResult.Error.Code.ShouldBe("Attribute.InvalidValue");
    }

    [Fact]
    public void Measure_ParsesLikeDecimal()
    {
        Parse(AttributeDataType.Measure, "38.5").Value.Number.ShouldBe(38.5m);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    public void Boolean_AcceptsBooleanLiterals(string raw, bool expected)
    {
        Parse(AttributeDataType.TrueFalse, raw).Value.Boolean.ShouldBe(expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void Boolean_RejectsAnythingElse(string raw)
    {
        Parse(AttributeDataType.TrueFalse, raw).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Date_ParsesIsoForm()
    {
        Parse(AttributeDataType.CalendarDate, "2026-06-22").Value.Date.ShouldBe(new DateOnly(2026, 6, 22));
    }

    [Theory]
    [InlineData("22/06/2026")]
    [InlineData("not-a-date")]
    public void Date_RejectsNonIsoForms(string raw)
    {
        Parse(AttributeDataType.CalendarDate, raw).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void List_AcceptsAKnownOption()
    {
        Parse(AttributeDataType.List, "ei90", FireRatings).Value.OptionCode.ShouldBe("ei90");
    }

    [Fact]
    public void List_RejectsAnUnknownOption()
    {
        var result = Parse(AttributeDataType.List, "ei200", FireRatings);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Attribute.UnknownOption");
    }

    [Theory]
    [InlineData(AttributeDataType.MultiList)]
    [InlineData(AttributeDataType.Reference)]
    public void UnsupportedTypes_AreRejectedClearly(string dataType)
    {
        var result = Parse(dataType, "anything");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Attribute.NotEditable");
    }

    [Fact]
    public void EditableSet_MatchesWhatTheParserHandles()
    {
        AttributeDataType.IsEditable(AttributeDataType.Text).ShouldBeTrue();
        AttributeDataType.IsEditable(AttributeDataType.MultiList).ShouldBeFalse();
        AttributeDataType.IsEditable(AttributeDataType.Reference).ShouldBeFalse();
    }
}
