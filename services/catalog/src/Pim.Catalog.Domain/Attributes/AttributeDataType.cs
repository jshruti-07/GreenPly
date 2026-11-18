using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Attributes;

/// <summary>
/// Mirrors the attribute.data_type CHECK constraint. Only the types this
/// slice can edit are listed as editable; the rest are recognised so the API
/// can report them honestly instead of failing obscurely.
/// </summary>
public static class AttributeDataType
{
    public const string Text = "text";
    public const string Number = "number";
    public const string DecimalNumber = "decimal";
    public const string TrueFalse = "boolean";
    public const string CalendarDate = "date";
    public const string Timestamp = "datetime";
    public const string List = "list";
    public const string MultiList = "multilist";
    public const string Measure = "measure";
    public const string Html = "html";
    public const string Reference = "reference";

    /// <summary>
    /// Editable in the Sprint 6 Attributes tab. multilist, html and reference
    /// need richer editors (chips, rich text, entity picker) and ship later;
    /// the API rejects writes to them rather than corrupting data.
    /// </summary>
    public static readonly IReadOnlySet<string> Editable = new HashSet<string>(StringComparer.Ordinal)
    {
        Text, Number, DecimalNumber, TrueFalse, CalendarDate, Timestamp, List, Measure,
    };

    public static bool IsEditable(string dataType) => Editable.Contains(dataType);
}

/// <summary>Errors raised while validating attribute values.</summary>
public static class AttributeErrors
{
    public static Error UnknownAttribute(string code) =>
        Error.Validation("Attribute.Unknown", $"Attribute '{code}' is not in this product's attribute set.");

    public static Error NotEditable(string code, string dataType) =>
        Error.Validation("Attribute.NotEditable",
            $"Attribute '{code}' has data type '{dataType}', which is not editable yet.");

    public static Error InvalidValue(string code, string dataType) =>
        Error.Validation("Attribute.InvalidValue",
            $"Value for '{code}' is not a valid {dataType}.");

    public static Error UnknownOption(string code, string optionCode) =>
        Error.Validation("Attribute.UnknownOption",
            $"'{optionCode}' is not a valid option for attribute '{code}'.");

    public static Error RequiredValueMissing(string code) =>
        Error.Validation("Attribute.Required", $"Attribute '{code}' is required and cannot be cleared.");
}
