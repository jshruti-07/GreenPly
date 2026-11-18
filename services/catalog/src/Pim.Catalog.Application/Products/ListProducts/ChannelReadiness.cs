namespace Pim.Catalog.Application.Products.ListProducts;

/// <summary>Per-channel readiness signal shown as dots in the grid.</summary>
public enum ChannelReadinessLevel
{
    /// <summary>Not ready — insufficient data.</summary>
    None = 0,

    /// <summary>Nearly ready — data complete enough but not published.</summary>
    Partial = 1,

    /// <summary>Ready to syndicate.</summary>
    Ready = 2,
}

/// <summary>
/// DERIVED, NOT AUTHORITATIVE.
///
/// The real signal belongs to the channel service, which does not exist yet
/// (ADR-004; blocked on the transactional outbox + RabbitMQ). Until then the
/// grid derives an indicative readiness from data the catalog service owns:
///
///   Ready   — completeness = 100% AND workflow = Published
///   Partial — completeness >= 80%
///   None    — everything else
///
/// The UI labels this column as derived. When the channel service ships, this
/// type is deleted and readiness comes from channel_product_status events;
/// nothing else in the read model depends on it.
/// </summary>
public static class ChannelReadiness
{
    /// <summary>Channels per the v4.2 prototype (Print is Phase 3, excluded).</summary>
    public static readonly IReadOnlyList<string> Channels =
        ["Website", "Dealer Portal", "Mobile App", "API Feed"];

    public const int PartialThresholdPct = 80;

    public static ChannelReadinessLevel Derive(int completenessPct, string workflowState)
    {
        if (completenessPct >= 100 &&
            string.Equals(workflowState, "Published", StringComparison.OrdinalIgnoreCase))
        {
            return ChannelReadinessLevel.Ready;
        }

        return completenessPct >= PartialThresholdPct
            ? ChannelReadinessLevel.Partial
            : ChannelReadinessLevel.None;
    }

    /// <summary>
    /// One level per channel. Every channel currently derives from the same
    /// inputs, so the values match; the shape is per-channel so the UI does not
    /// change when real per-channel data arrives.
    /// </summary>
    public static IReadOnlyList<ChannelReadinessLevel> DeriveAll(int completenessPct, string workflowState)
    {
        var level = Derive(completenessPct, workflowState);
        return [.. Enumerable.Repeat(level, Channels.Count)];
    }
}
