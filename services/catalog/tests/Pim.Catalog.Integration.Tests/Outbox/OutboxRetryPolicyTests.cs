using Pim.Catalog.Infrastructure.Outbox;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests.Outbox;

/// <summary>
/// Backoff arithmetic. Small enough to read, and pinned anyway because two of
/// these cases are reachable by configuration alone and neither is obvious.
/// </summary>
public sealed class OutboxRetryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]     // first failure waits one base period, not zero
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(8, 256)]
    public void Doubles_EachAttempt(int attempt, int expected) =>
        OutboxRetryPolicy.NextDelaySeconds(attempt, baseSeconds: 2, maxSeconds: 100_000)
            .ShouldBe(expected);

    [Fact]
    public void IsCapped()
    {
        OutboxRetryPolicy.NextDelaySeconds(attemptNumber: 20, baseSeconds: 2, maxSeconds: 300)
            .ShouldBe(300);
    }

    [Fact]
    public void VeryLargeAttemptNumbers_DoNotOverflowIntoAShortDelay()
    {
        // The bug this guards: 1 << 64 is not 0, it is 1 << (64 % 32) — so a
        // naive shift turns attempt 65 into the shortest possible retry and
        // hammers a broker that is already failing. The clamp must hold for
        // any attempt count MaxAttempts allows.
        var delay = OutboxRetryPolicy.NextDelaySeconds(attemptNumber: 100, baseSeconds: 2, maxSeconds: 300);

        delay.ShouldBe(300);
    }

    [Fact]
    public void CapBelowBase_YieldsTheCap_NotANegativeOrZeroDelay()
    {
        // Misconfiguration, rejected by OutboxOptionsValidator — but the policy
        // must still return something sane if it is ever called directly.
        OutboxRetryPolicy.NextDelaySeconds(attemptNumber: 1, baseSeconds: 60, maxSeconds: 5)
            .ShouldBe(5);
    }

    [Theory]
    [InlineData(0, 2, 300)]
    [InlineData(1, 0, 300)]
    [InlineData(1, 2, 0)]
    public void RejectsNonsenseArguments(int attempt, int baseSeconds, int maxSeconds) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => OutboxRetryPolicy.NextDelaySeconds(attempt, baseSeconds, maxSeconds));
}
