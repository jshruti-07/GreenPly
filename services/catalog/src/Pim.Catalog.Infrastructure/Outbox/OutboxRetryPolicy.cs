namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Backoff arithmetic, extracted as a pure function so it can be tested without
/// a database, a broker, or the passage of time. It is small enough to look
/// obviously right and was still worth pinning: the overflow case below is not
/// obvious, and it is reached by configuration alone.
/// </summary>
public static class OutboxRetryPolicy
{
    /// <summary>
    /// Delay before the next attempt: <c>base * 2^(attempt-1)</c>, capped.
    /// </summary>
    /// <param name="attemptNumber">
    /// The attempt that just failed, 1-based. Attempt 1 waits one base period.
    /// </param>
    public static int NextDelaySeconds(int attemptNumber, int baseSeconds, int maxSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(baseSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSeconds, 1);

        // Shifting past the width of the type is undefined-ish rather than
        // saturating, and a large MaxAttempts is a configuration value, not a
        // code path anyone reviews. Clamp the exponent before computing.
        // 2^30 seconds already exceeds any sane cap by orders of magnitude.
        var exponent = Math.Min(attemptNumber - 1, 30);

        var delay = (long)baseSeconds << exponent;

        return (int)Math.Min(delay, maxSeconds);
    }
}
