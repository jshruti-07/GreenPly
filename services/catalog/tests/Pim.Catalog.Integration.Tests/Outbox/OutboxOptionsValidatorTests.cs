using Pim.Catalog.Infrastructure.Outbox;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests.Outbox;

/// <summary>
/// The validator exists because every bad value here fails quietly at runtime.
/// These tests are cheap; the outage they prevent is a queue that appears to be
/// running and is delivering nothing.
/// </summary>
public sealed class OutboxOptionsValidatorTests
{
    private readonly OutboxOptionsValidator _validator = new();

    [Fact]
    public void TheShippedDefaults_AreValid()
    {
        // If this ever fails, the defaults in appsettings.json are unusable and
        // the service would refuse to start on a clean checkout.
        _validator.Validate(name: null, new OutboxOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void ABatchSizeOfZero_IsRejected()
    {
        var result = _validator.Validate(null, new OutboxOptions { BatchSize = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(OutboxOptions.BatchSize));
    }

    [Fact]
    public void ABackoffCapBelowTheBase_IsRejected()
    {
        // Would silently flatten exponential backoff into a fixed delay.
        var result = _validator.Validate(
            null, new OutboxOptions { BackoffBaseSeconds = 60, BackoffMaxSeconds = 10 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(OutboxOptions.BackoffMaxSeconds));
    }

    [Fact]
    public void ALeaseShorterThanTheFirstRetry_IsRejected()
    {
        // The subtle one: leases would lapse and republish before the backoff
        // they were told to wait for had elapsed, so a failing broker gets
        // hammered by exactly the mechanism meant to back off from it.
        var result = _validator.Validate(
            null, new OutboxOptions { LeaseSeconds = 5, BackoffBaseSeconds = 30 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(OutboxOptions.LeaseSeconds));
    }

    [Fact]
    public void AllFailuresAreReportedTogether()
    {
        var result = _validator.Validate(
            null, new OutboxOptions { BatchSize = 0, MaxAttempts = 0, LeaseSeconds = 1 });

        // One restart per misconfigured field is a bad way to learn the rules.
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(OutboxOptions.BatchSize));
        result.FailureMessage.ShouldContain(nameof(OutboxOptions.MaxAttempts));
    }
}
