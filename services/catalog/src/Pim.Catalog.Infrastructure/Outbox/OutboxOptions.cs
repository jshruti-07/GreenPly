namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Relay tuning. Every value is validated by <see cref="OutboxOptionsValidator"/>
/// rather than trusted, because the failure mode of a bad one is silent: a zero
/// batch size or a negative interval produces a relay that runs, logs nothing
/// unusual, and delivers nothing. The ranges quoted in the comments below are
/// the ones that validator enforces.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Set false to stop the relay from starting. Intended for tests and for
    /// running the API alongside a separate relay process later; it is NOT a
    /// way to turn messaging off in an environment that expects delivery.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Rows claimed per pass. Larger batches amortise the round trip but widen
    /// the window in which a relay crash strands leased rows until their lease
    /// lapses.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Idle pause between passes. Only applies when the last pass found no
    /// work: a full batch is followed immediately by another pass, so a burst
    /// drains at broker speed rather than at poll speed.
    /// </summary>
    public int PollIntervalMilliseconds { get; set; } = 1_000;

    /// <summary>
    /// How long a claim is held before another relay may take the row. Must
    /// comfortably exceed the slowest realistic publish, or two relays will
    /// publish the same message concurrently — which consumers survive, since
    /// delivery is at-least-once, but which wastes work and confuses tracing.
    /// </summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>
    /// Attempts before a message is dead-lettered. Dead is a terminal, visible
    /// state — not a deletion. Something that could never be published is an
    /// operational fact somebody has to see.
    /// </summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Base for exponential backoff: delay = Base * 2^(attempt-1).</summary>
    public int BackoffBaseSeconds { get; set; } = 2;

    /// <summary>
    /// Ceiling on backoff. Without it, attempt 8 of a 2-second base lands 4.3
    /// minutes out and attempt 20 would land days out, which is indistinguishable
    /// from lost.
    /// </summary>
    public int BackoffMaxSeconds { get; set; } = 300;

    /// <summary>
    /// Pending depth at which the health check degrades. A backlog is not an
    /// error — a burst of edits creates one legitimately — so this reports
    /// Degraded, not Unhealthy, and never fails the readiness probe on its own.
    /// </summary>
    public int HealthyPendingThreshold { get; set; } = 1_000;

    /// <summary>
    /// Age of the oldest pending message at which the health check degrades.
    /// Depth alone misses the important case: a small backlog that is not
    /// moving at all.
    /// </summary>
    public int HealthyOldestPendingSeconds { get; set; } = 300;
}
