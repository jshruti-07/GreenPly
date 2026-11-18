using System.Globalization;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Attributes;

/// <summary>The typed column an attribute value lands in.</summary>
public sealed record ParsedAttributeValue(
    string? Text = null,
    decimal? Number = null,
    bool? Boolean = null,
    DateOnly? Date = null,
    DateTimeOffset? DateTime = null,
    string? OptionCode = null)
{
    /// <summary>True when the editor cleared the field.</summary>
    public bool IsEmpty =>
        Text is null && Number is null && Boolean is null &&
        Date is null && DateTime is null && OptionCode is null;
}

/// <summary>
/// Turns the wire value (always a string, or null to clear) into the typed
/// column dictated by the attribute's data_type.
///
/// Parsing is INVARIANT-culture on purpose: "1.5" must mean one-and-a-half
/// regardless of the server's locale, and the German UI posts canonical
/// values. Locale-aware display formatting is the client's job.
/// </summary>
public static class AttributeValueParser
{
    public static Result<ParsedAttributeValue> Parse(
        string attributeCode,
        string dataType,
        string? rawValue,
        IReadOnlyCollection<string> allowedOptionCodes)
    {
        ArgumentNullException.ThrowIfNull(allowedOptionCodes);

        if (!AttributeDataType.IsEditable(dataType))
        {
            return Result.Failure<ParsedAttributeValue>(
                AttributeErrors.NotEditable(attributeCode, dataType));
        }

        var value = rawValue?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return new ParsedAttributeValue(); // cleared
        }

        switch (dataType)
        {
            case AttributeDataType.Text:
            case AttributeDataType.Html:
                return new ParsedAttributeValue(Text: value);

            case AttributeDataType.Number:
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? new ParsedAttributeValue(Number: whole)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.InvalidValue(attributeCode, dataType));

            case AttributeDataType.DecimalNumber:
            case AttributeDataType.Measure:
                return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
                    ? new ParsedAttributeValue(Number: dec)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.InvalidValue(attributeCode, dataType));

            case AttributeDataType.TrueFalse:
                return bool.TryParse(value, out var flag)
                    ? new ParsedAttributeValue(Boolean: flag)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.InvalidValue(attributeCode, dataType));

            case AttributeDataType.CalendarDate:
                return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? new ParsedAttributeValue(Date: date)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.InvalidValue(attributeCode, dataType));

            case AttributeDataType.Timestamp:
                return DateTimeOffset.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
                    ? new ParsedAttributeValue(DateTime: moment)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.InvalidValue(attributeCode, dataType));

            case AttributeDataType.List:
                return allowedOptionCodes.Contains(value, StringComparer.Ordinal)
                    ? new ParsedAttributeValue(OptionCode: value)
                    : Result.Failure<ParsedAttributeValue>(
                        AttributeErrors.UnknownOption(attributeCode, value));

            default:
                return Result.Failure<ParsedAttributeValue>(
                    AttributeErrors.NotEditable(attributeCode, dataType));
        }
    }
}
