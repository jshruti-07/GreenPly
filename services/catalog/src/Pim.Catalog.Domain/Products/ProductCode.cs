using System.Text.RegularExpressions;
using Pim.SharedKernel.Domain;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Products;

/// <summary>
/// Business identifier of a product (e.g. "FD-90-GS-1000"). Uppercase
/// alphanumeric segments separated by hyphens; unique across the catalogue.
/// </summary>
public sealed partial class ProductCode : ValueObject
{
    public const int MaxLength = 64;

    private ProductCode(string value) => Value = value;

    public string Value { get; }

    public static Result<ProductCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ProductCode>(ProductErrors.CodeEmpty);
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > MaxLength)
        {
            return Result.Failure<ProductCode>(ProductErrors.CodeTooLong);
        }

        if (!CodePattern().IsMatch(normalized))
        {
            return Result.Failure<ProductCode>(ProductErrors.CodeInvalidFormat);
        }

        return new ProductCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex CodePattern();
}
