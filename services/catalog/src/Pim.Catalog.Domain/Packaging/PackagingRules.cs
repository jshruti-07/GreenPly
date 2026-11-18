using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Packaging;

/// <summary>
/// Packaging levels, outermost last. The order is meaningful: a carton contains
/// eaches, a pallet contains cartons. Storing the order here rather than relying
/// on the enum's declaration order makes the containment rule explicit and
/// testable.
/// </summary>
public static class PackagingLevel
{
    public const string Each = "EACH";
    public const string Inner = "INNER";
    public const string Carton = "CARTON";
    public const string Layer = "LAYER";
    public const string Pallet = "PALLET";

    /// <summary>Innermost to outermost.</summary>
    public static readonly string[] Ordered = [Each, Inner, Carton, Layer, Pallet];

    public static bool IsKnown(string level) =>
        Array.Exists(Ordered, l => string.Equals(l, level, StringComparison.Ordinal));

    public static int RankOf(string level) => Array.IndexOf(Ordered, level);

    /// <summary>
    /// A level may only be contained by a strictly larger one. Without this a
    /// pallet could be recorded inside an each, and the packaging hierarchy
    /// would produce nonsense freight and quantity calculations downstream.
    /// </summary>
    public static bool CanContain(string outer, string inner) =>
        IsKnown(outer) && IsKnown(inner) && RankOf(outer) > RankOf(inner);
}

/// <summary>
/// GS1 identifier checks. Deliberately structural only — length and digits.
/// A real check-digit validation belongs here too, but it is only worth adding
/// once we import GS1 data; inventing it now would give false confidence about
/// hand-typed demo values.
/// </summary>
public static class Gs1
{
    public const int SsccLength = 18;

    /// <summary>GTIN-8, -12, -13 or -14.</summary>
    public static bool IsWellFormedGtin(string value) =>
        value.Length is >= 8 and <= 14 && value.All(char.IsAsciiDigit);

    /// <summary>SSCC is always exactly 18 digits.</summary>
    public static bool IsWellFormedSscc(string value) =>
        value.Length == SsccLength && value.All(char.IsAsciiDigit);
}

public static class PackagingErrors
{
    public static Error UnknownLevel(string level) =>
        Error.Validation("Packaging.UnknownLevel",
            $"'{level}' is not a packaging level. Expected one of: {string.Join(", ", PackagingLevel.Ordered)}.");

    public static Error DuplicateLevel(string level) =>
        Error.Conflict("Packaging.DuplicateLevel",
            $"This SKU already has a {level} level. Edit it instead of adding a second one.");

    public static Error InvalidContainment(string outer, string inner) =>
        Error.Validation("Packaging.InvalidContainment",
            $"A {outer} cannot be contained by a {inner}; packaging must nest from smaller to larger.");

    public static readonly Error InvalidGtin =
        Error.Validation("Packaging.InvalidGtin", "GTIN must be 8 to 14 digits.");

    public static readonly Error InvalidSscc =
        Error.Validation("Packaging.InvalidSscc", "SSCC must be exactly 18 digits.");

    public static readonly Error GrossBelowNet =
        Error.Validation("Packaging.GrossBelowNet",
            "Gross weight cannot be less than net weight — gross includes the packaging.");

    public static Error GtinInUse(string gtin) =>
        Error.Conflict("Packaging.GtinInUse",
            $"GTIN '{gtin}' is already used by another packaging unit. A GTIN identifies exactly one tradeable unit.");

    public static Error SsccInUse(string sscc) =>
        Error.Conflict("Packaging.SsccInUse", $"SSCC '{sscc}' is already used by another packaging unit.");

    public static Error NotFound(long id) =>
        Error.NotFound("Packaging.NotFound", $"Packaging unit {id} was not found on this product.");

    public static Error SkuNotFound(string skuCode) =>
        Error.NotFound("Packaging.SkuNotFound", $"SKU '{skuCode}' was not found on this product.");

    public static readonly Error QuantityNotPositive =
        Error.Validation("Packaging.QuantityNotPositive", "Contained quantity must be greater than zero.");
}

/// <summary>Relationship kinds a product can have to another product.</summary>
public static class RelationType
{
    public const string Accessory = "ACCESSORY";
    public const string Compatible = "COMPATIBLE";
    public const string Replacement = "REPLACEMENT";
    public const string CrossSell = "CROSS_SELL";
    public const string UpSell = "UP_SELL";

    public static readonly string[] All = [Accessory, Compatible, Replacement, CrossSell, UpSell];

    public static bool IsKnown(string relationType) =>
        Array.Exists(All, t => string.Equals(t, relationType, StringComparison.Ordinal));
}

public static class RelationshipErrors
{
    public static Error UnknownType(string relationType) =>
        Error.Validation("Relationship.UnknownType",
            $"'{relationType}' is not a relationship type. Expected one of: {string.Join(", ", RelationType.All)}.");

    public static readonly Error SelfReference =
        Error.Validation("Relationship.SelfReference", "A product cannot be related to itself.");

    public static Error Duplicate(string targetCode, string relationType) =>
        Error.Conflict("Relationship.Duplicate",
            $"'{targetCode}' is already linked as {relationType}.");

    public static Error TargetNotFound(string code) =>
        Error.NotFound("Relationship.TargetNotFound", $"Product '{code}' was not found.");

    public static Error NotFound(string targetCode, string relationType) =>
        Error.NotFound("Relationship.NotFound",
            $"No {relationType} link to '{targetCode}' exists on this product.");
}

public static class ApplicationErrors
{
    public static Error UnknownApplication(string code) =>
        Error.Validation("Application.Unknown", $"Application '{code}' is not configured.");
}
