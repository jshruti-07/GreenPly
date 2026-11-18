using System.Globalization;
using Microsoft.Extensions.Options;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Validates <see cref="OutboxOptions"/> on first resolution.
///
/// Hand-written rather than <c>ValidateDataAnnotations</c> because that
/// extension lives outside the packages this project already restores, and an
/// unvalidated options object is a worse outcome than a few explicit ifs. The
/// failure being guarded against is quiet: a zero batch size produces a relay
/// that starts, polls, claims nothing and logs nothing unusual, forever.
/// </summary>
public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        Require(failures, options.BatchSize, 1, 1_000, nameof(options.BatchSize));
        Require(failures, options.PollIntervalMilliseconds, 50, 60_000, nameof(options.PollIntervalMilliseconds));
        Require(failures, options.LeaseSeconds, 5, 3_600, nameof(options.LeaseSeconds));
        Require(failures, options.MaxAttempts, 1, 100, nameof(options.MaxAttempts));
        Require(failures, options.BackoffBaseSeconds, 1, 3_600, nameof(options.BackoffBaseSeconds));
        Require(failures, options.BackoffMaxSeconds, 1, 86_400, nameof(options.BackoffMaxSeconds));
        Require(failures, options.HealthyPendingThreshold, 1, 10_000_000, nameof(options.HealthyPendingThreshold));
        Require(failures, options.HealthyOldestPendingSeconds, 1, 86_400, nameof(options.HealthyOldestPendingSeconds));

        if (options.BackoffMaxSeconds < options.BackoffBaseSeconds)
        {
            failures.Add(
                $"{nameof(options.BackoffMaxSeconds)} ({options.BackoffMaxSeconds}) is below " +
                $"{nameof(options.BackoffBaseSeconds)} ({options.BackoffBaseSeconds}), which would cap the very " +
                "first retry and make backoff a fixed delay.");
        }

        // A lease shorter than the retry delay lets a message be reclaimed and
        // republished before its own backoff has elapsed, which quietly defeats
        // the backoff under exactly the conditions it exists for.
        if (options.LeaseSeconds < options.BackoffBaseSeconds)
        {
            failures.Add(
                $"{nameof(options.LeaseSeconds)} ({options.LeaseSeconds}) is below " +
                $"{nameof(options.BackoffBaseSeconds)} ({options.BackoffBaseSeconds}); leases would lapse faster " +
                "than the first retry is scheduled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(List<string> failures, int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            failures.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} must be between {1} and {2}; got {3}.",
                name,
                min,
                max,
                value));
        }
    }
}
