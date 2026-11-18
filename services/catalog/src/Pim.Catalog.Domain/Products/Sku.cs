using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Products;

/// <summary>
/// A saleable variant of a product (`sku`). Part of the Product aggregate:
/// SKUs are created, renamed and retired through the product, never
/// independently, because their uniqueness and ordering are product-scoped.
///
/// Variant AXIS VALUES (colour, width, ...) live in attribute_value keyed by
/// sku_id and are handled by the attribute store, not here — this entity owns
/// the SKU's own identity and commercial state.
/// </summary>
public sealed class Sku
{
    public const int MaxCodeLength = 64;

    private Sku()
    {
        // EF Core materialization only.
        Code = null!;
    }

    public long Id { get; private set; }

    public long ProductId { get; private set; }

    public string Code { get; private set; }

    public string? Gtin { get; private set; }

    public string? ErpMaterialNo { get; private set; }

    public SkuStatus Status { get; private set; }

    public int SortOrder { get; private set; }

    internal static Result<Sku> Create(
        string code,
        string? gtin,
        string? erpMaterialNo,
        int sortOrder)
    {
        var normalized = Normalize(code);
        if (normalized.IsFailure)
        {
            return Result.Failure<Sku>(normalized.Error);
        }

        var gtinResult = ValidateGtin(gtin);
        if (gtinResult.IsFailure)
        {
            return Result.Failure<Sku>(gtinResult.Error);
        }

        return new Sku
        {
            Code = normalized.Value,
            Gtin = Blank(gtin),
            ErpMaterialNo = Blank(erpMaterialNo),
            Status = SkuStatus.Active,
            SortOrder = sortOrder,
        };
    }

    internal Result Update(string? gtin, string? erpMaterialNo, SkuStatus status, int sortOrder)
    {
        var gtinResult = ValidateGtin(gtin);
        if (gtinResult.IsFailure)
        {
            return gtinResult;
        }

        Gtin = Blank(gtin);
        ErpMaterialNo = Blank(erpMaterialNo);
        Status = status;
        SortOrder = sortOrder;
        return Result.Success();
    }

    /// <summary>
    /// SKU codes follow the product-code convention: uppercase alphanumeric
    /// segments separated by hyphens. Kept deliberately strict — these end up
    /// in ERP feeds and URLs.
    /// </summary>
    private static Result<string> Normalize(string code)
    {
        var trimmed = code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Result.Failure<string>(SkuErrors.CodeEmpty);
        }

        if (trimmed.Length > MaxCodeLength)
        {
            return Result.Failure<string>(SkuErrors.CodeTooLong);
        }

        var segments = trimmed.Split('-');
        if (segments.Any(segment => segment.Length == 0 || !segment.All(char.IsLetterOrDigit)))
        {
            return Result.Failure<string>(SkuErrors.CodeInvalidFormat);
        }

        return trimmed;
    }

    private static Result ValidateGtin(string? gtin)
    {
        var value = Blank(gtin);
        if (value is null)
        {
            return Result.Success();
        }

        return value.Length is >= 8 and <= 14 && value.All(char.IsDigit)
            ? Result.Success()
            : Result.Failure(SkuErrors.GtinInvalid);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class SkuErrors
{
    public static readonly Error CodeEmpty =
        Error.Validation("Sku.Code.Empty", "A SKU code is required.");

    public static readonly Error CodeTooLong =
        Error.Validation("Sku.Code.TooLong", $"SKU code must not exceed {Sku.MaxCodeLength} characters.");

    public static readonly Error CodeInvalidFormat =
        Error.Validation("Sku.Code.InvalidFormat",
            "SKU code must be uppercase alphanumeric segments separated by hyphens (e.g. FD-90-GS-1000-L).");

    public static readonly Error GtinInvalid =
        Error.Validation("Sku.Gtin.Invalid", "GTIN must be 8-14 digits.");

    public static readonly Error DuplicateCode =
        Error.Conflict("Sku.Code.Duplicate", "A SKU with this code already exists on this product.");

    public static Error NotFound(string code) =>
        Error.NotFound("Sku.NotFound", $"Variant '{code}' was not found on this product.");
}
