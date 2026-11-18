namespace Pim.Catalog.Domain.Products;

/// <summary>
/// Commercial state of a variant (sku.status: active·blocked·eol).
///
/// Distinct from the product's lifecycle: a product can be Active while one of
/// its SKUs is blocked (e.g. a colour withdrawn by the supplier).
/// </summary>
public enum SkuStatus
{
    Active,
    Blocked,

    /// <summary>End of life — no longer produced, may still be serviced.</summary>
    EndOfLife,
}

public static class SkuStatusMap
{
    /// <summary>Domain enum → the value stored in sku.status.</summary>
    public static string ToDb(SkuStatus status) => status switch
    {
        SkuStatus.Active => "active",
        SkuStatus.Blocked => "blocked",
        SkuStatus.EndOfLife => "eol",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped SKU status."),
    };

    public static SkuStatus FromDb(string value) => value switch
    {
        "active" => SkuStatus.Active,
        "blocked" => SkuStatus.Blocked,
        "eol" => SkuStatus.EndOfLife,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unmapped SKU status."),
    };
}
