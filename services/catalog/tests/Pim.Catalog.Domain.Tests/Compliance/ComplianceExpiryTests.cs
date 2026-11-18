using Pim.Catalog.Domain.Compliance;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Compliance;

/// <summary>
/// Expiry classification is the one piece of real business logic in the
/// compliance module, and a wrong answer means either a false alarm or, worse,
/// shipping against a lapsed certificate. The boundaries are pinned exactly.
/// </summary>
public sealed class ComplianceExpiryTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    [Fact]
    public void NoEndDate_NeverExpires()
    {
        // A Declaration of Performance carries no expiry by design.
        ComplianceExpiry.Classify(null, Today).ShouldBe(ExpiryState.NoExpiry);
    }

    [Fact]
    public void ExpiringToday_IsStillValid_NotExpired()
    {
        // Inclusive boundary: a certificate valid to 15 Aug is valid ON 15 Aug.
        ComplianceExpiry.Classify(Today, Today).ShouldBe(ExpiryState.Expiring);
    }

    [Fact]
    public void Yesterday_IsExpired()
    {
        ComplianceExpiry.Classify(Today.AddDays(-1), Today).ShouldBe(ExpiryState.Expired);
    }

    [Fact]
    public void ExactlyTwelveMonthsOut_IsExpiring()
    {
        // The window is inclusive at its far edge, so the last day that still
        // warns is unambiguous rather than off by one.
        var boundary = Today.AddMonths(ComplianceExpiry.WarningWindowMonths);

        ComplianceExpiry.Classify(boundary, Today).ShouldBe(ExpiryState.Expiring);
    }

    [Fact]
    public void OneDayBeyondTheWindow_IsValid()
    {
        var justOutside = Today.AddMonths(ComplianceExpiry.WarningWindowMonths).AddDays(1);

        ComplianceExpiry.Classify(justOutside, Today).ShouldBe(ExpiryState.Valid);
    }

    [Fact]
    public void FarFuture_IsValid()
    {
        ComplianceExpiry.Classify(new DateOnly(2035, 1, 1), Today).ShouldBe(ExpiryState.Valid);
    }

    [Theory]
    [InlineData("DOP")]
    [InlineData("CE")]
    [InlineData("EPD")]
    [InlineData("DPP")]
    [InlineData("TEST")]
    [InlineData("UKCA")]
    public void KnownDocTypes_AreAccepted(string docType) =>
        ComplianceDocType.IsKnown(docType).ShouldBeTrue();

    [Theory]
    [InlineData("dop")]   // the DB CHECK is case-sensitive, so this must not pass
    [InlineData("ISO")]
    [InlineData("")]
    public void UnknownOrMiscasedDocTypes_AreRejected(string docType) =>
        ComplianceDocType.IsKnown(docType).ShouldBeFalse();

    [Theory]
    [InlineData("valid")]
    [InlineData("withdrawn")]
    [InlineData("superseded")]
    public void KnownStatuses_AreAccepted(string status) =>
        ComplianceStatus.IsKnown(status).ShouldBeTrue();

    [Theory]
    [InlineData("expired")]  // expiry is derived; it is not a storable status
    [InlineData("Valid")]
    public void UnknownStatuses_AreRejected(string status) =>
        ComplianceStatus.IsKnown(status).ShouldBeFalse();
}
