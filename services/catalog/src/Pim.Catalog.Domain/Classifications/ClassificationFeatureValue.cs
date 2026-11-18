using System.Globalization;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Classifications;

/// <summary>
/// ETIM-style feature data types. These are NOT the same set as our internal
/// attribute data types and are deliberately kept separate: they are defined by
/// the external standard, and collapsing the two vocabularies would mean a
/// change to ETIM could silently alter how our own attributes behave.
/// </summary>
public static class ClassificationDataType
{
    public const string Numeric = "numeric";
    public const string Alpha = "alpha";
    public const string Logical = "logical";
    public const string Range = "range";

    public static bool IsKnown(string dataType) =>
        dataType is Numeric or Alpha or Logical or Range;
}

/// <summary>
/// A feature value parsed into the column it belongs in. Exactly one of the
/// three value slots is populated, or none when the input was empty.
/// </summary>
public sealed record ParsedFeatureValue(string? Text, decimal? Number, bool? Boolean)
{
    public static readonly ParsedFeatureValue Empty = new(null, null, null);

    public bool IsEmpty => Text is null && Number is null && Boolean is null;
}

public static class ClassificationFeatureValueParser
{
    /// <summary>Longest accepted text/range value; guards the text column and the UI.</summary>
    public const int MaxTextLength = 512;

    /// <summary>
    /// Parses a client-supplied string into the typed slot for the feature.
    ///
    /// A range ('10-20') stays text on purpose: the standard defines it as a
    /// pair, and splitting it into two numeric columns would require schema we
    /// do not have and cannot round-trip back to the exchange format.
    /// </summary>
    public static Result<ParsedFeatureValue> Parse(string featureCode, string dataType, string? value)
    {
        if (!ClassificationDataType.IsKnown(dataType))
        {
            return Result.Failure<ParsedFeatureValue>(
                ClassificationErrors.UnknownDataType(featureCode, dataType));
        }

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success(ParsedFeatureValue.Empty);
        }

        if (trimmed.Length > MaxTextLength)
        {
            return Result.Failure<ParsedFeatureValue>(ClassificationErrors.ValueTooLong(featureCode));
        }

        switch (dataType)
        {
            case ClassificationDataType.Numeric:
                // Invariant culture only: the API contract is a canonical string,
                // not the steward's locale. Locale formatting is a UI concern.
                return decimal.TryParse(
                    trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? Result.Success(new ParsedFeatureValue(null, number, null))
                    : Result.Failure<ParsedFeatureValue>(
                        ClassificationErrors.InvalidValue(featureCode, dataType));

            case ClassificationDataType.Logical:
                return trimmed.ToUpperInvariant() switch
                {
                    "TRUE" or "YES" or "1" => Result.Success(new ParsedFeatureValue(null, null, true)),
                    "FALSE" or "NO" or "0" => Result.Success(new ParsedFeatureValue(null, null, false)),
                    _ => Result.Failure<ParsedFeatureValue>(
                        ClassificationErrors.InvalidValue(featureCode, dataType)),
                };

            case ClassificationDataType.Range:
                return IsWellFormedRange(trimmed)
                    ? Result.Success(new ParsedFeatureValue(trimmed, null, null))
                    : Result.Failure<ParsedFeatureValue>(
                        ClassificationErrors.InvalidValue(featureCode, dataType));

            default:
                return Result.Success(new ParsedFeatureValue(trimmed, null, null));
        }
    }

    /// <summary>'10-20' or '10.5-20.5' — two invariant numbers, low first.</summary>
    private static bool IsWellFormedRange(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!decimal.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var low) ||
            !decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
        {
            return false;
        }

        return low <= high;
    }

    /// <summary>
    /// Canonical string form of a stored value, matching what the read SQL
    /// produces. Shared so tests assert one rule rather than two.
    /// </summary>
    public static string? Format(ParsedFeatureValue parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (parsed.Boolean is { } flag)
        {
            return flag ? "true" : "false";
        }

        if (parsed.Number is { } number)
        {
            // Same rule as the attribute reader: trailing zeros may only be
            // stripped from the fraction, never from the whole number.
            return number == decimal.Truncate(number)
                ? decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture)
                : number.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        return parsed.Text;
    }
}

public static class ClassificationErrors
{
    public static Error SchemeNotFound(string code) =>
        Error.NotFound("Classification.SchemeNotFound", $"Classification scheme '{code}' is not configured.");

    public static Error ClassNotFound(string schemeCode, string classCode) =>
        Error.NotFound("Classification.ClassNotFound",
            $"Class '{classCode}' does not exist in scheme '{schemeCode}'.");

    public static Error UnknownFeature(string code) =>
        Error.Validation("Classification.UnknownFeature",
            $"Feature '{code}' is not declared by this product's assigned class.");

    public static Error UnknownDataType(string featureCode, string dataType) =>
        Error.Validation("Classification.UnknownDataType",
            $"Feature '{featureCode}' has unsupported data type '{dataType}'.");

    public static Error InvalidValue(string featureCode, string dataType) =>
        Error.Validation("Classification.InvalidValue",
            $"Value for '{featureCode}' is not a valid {dataType}.");

    public static Error ValueTooLong(string featureCode) =>
        Error.Validation("Classification.ValueTooLong",
            $"Value for '{featureCode}' exceeds {ClassificationFeatureValueParser.MaxTextLength} characters.");

    public static Error NoClassAssigned(string schemeCode) =>
        Error.Validation("Classification.NoClassAssigned",
            $"Assign a class in '{schemeCode}' before setting feature values.");
}
