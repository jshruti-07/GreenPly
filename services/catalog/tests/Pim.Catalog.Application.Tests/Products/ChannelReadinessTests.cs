using Pim.Catalog.Application.Products.ListProducts;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

/// <summary>
/// Pins the DERIVED placeholder rule so a change is deliberate. Replaced
/// wholesale when the channel service ships.
/// </summary>
public sealed class ChannelReadinessTests
{
    [Fact]
    public void Ready_RequiresFullCompletenessAndPublished()
    {
        ChannelReadiness.Derive(100, "Published").ShouldBe(ChannelReadinessLevel.Ready);
    }

    [Theory]
    [InlineData(100, "Approval")]
    [InlineData(100, "Draft")]
    public void FullyCompleteButUnpublished_IsPartial(int pct, string workflow)
    {
        ChannelReadiness.Derive(pct, workflow).ShouldBe(ChannelReadinessLevel.Partial);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(99)]
    public void AtOrAboveThreshold_IsPartial(int pct)
    {
        ChannelReadiness.Derive(pct, "Draft").ShouldBe(ChannelReadinessLevel.Partial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(79)]
    public void BelowThreshold_IsNone(int pct)
    {
        ChannelReadiness.Derive(pct, "Published").ShouldBe(ChannelReadinessLevel.None);
    }

    [Fact]
    public void DeriveAll_ReturnsOneLevelPerPrototypeChannel()
    {
        var levels = ChannelReadiness.DeriveAll(100, "Published");

        levels.Count.ShouldBe(4); // Website, Dealer Portal, Mobile App, API Feed
        levels.ShouldAllBe(l => l == ChannelReadinessLevel.Ready);
        ChannelReadiness.Channels.Count.ShouldBe(levels.Count);
    }
}
