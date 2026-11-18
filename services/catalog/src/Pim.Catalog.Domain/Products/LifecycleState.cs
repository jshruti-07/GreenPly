namespace Pim.Catalog.Domain.Products;

/// <summary>
/// Product lifecycle per the approved database design (product.lifecycle_state).
/// Allowed transitions: Planned -> Active -> PhaseOut -> Discontinued.
/// </summary>
public enum LifecycleState
{
    Planned,
    Active,
    PhaseOut,
    Discontinued
}
