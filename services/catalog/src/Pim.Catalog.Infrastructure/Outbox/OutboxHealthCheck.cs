using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Reports whether the outbox is draining.
///
/// Deliberately never returns Unhealthy for a backlog. A queue with work in it
/// is a queue doing its job, and wiring depth to a readiness probe means a
/// legitimate burst of edits takes the API out of the load balancer — turning a
/// throughput blip into an outage. Depth and staleness report Degraded; only
/// the inability to query the outbox at all is Unhealthy, because that means
/// the check itself knows nothing.
///
/// Dead letters are surfaced in the data every time, at any count. They are the
/// number that should generate an alert, and they never resolve on their own.
/// </summary>
public sealed class OutboxHealthCheck(
    OutboxStore store,
    IOutboxTransport transport,
    IOptions<OutboxOptions> options) : IHealthCheck
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        OutboxDepth depth;

        try
        {
            depth = await store.GetDepthAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Any failure to read depth is the same answer: we cannot tell.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Could not read outbox depth.", ex);
        }

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["pending"] = depth.Pending,
            ["publishing"] = depth.Publishing,
            ["dead"] = depth.Dead,
            ["oldestPendingAgeSeconds"] = depth.OldestPendingAgeSeconds ?? 0d,
            ["transport"] = transport.Description,
            ["deliversExternally"] = transport.IsDeliveringExternally
        };

        // Said plainly, not buried in a log line at startup. A drained queue
        // with no broker behind it is indistinguishable from a healthy system
        // unless something says so here.
        if (!transport.IsDeliveringExternally)
        {
            return HealthCheckResult.Degraded(
                $"Outbox is draining to '{transport.Description}'. No message is reaching another service.",
                data: data);
        }

        if (depth.Dead > 0)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} outbox message(s) dead-lettered and never delivered.",
                    depth.Dead),
                data: data);
        }

        if (depth.Pending > _options.HealthyPendingThreshold)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Outbox backlog is {0}, above the threshold of {1}.",
                    depth.Pending,
                    _options.HealthyPendingThreshold),
                data: data);
        }

        var oldest = depth.OldestPendingAgeSeconds ?? 0d;

        if (oldest > _options.HealthyOldestPendingSeconds)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Oldest pending outbox message is {0:F0}s old, above the threshold of {1}s. " +
                    "A small queue that is not moving is worse than a large one that is.",
                    oldest,
                    _options.HealthyOldestPendingSeconds),
                data: data);
        }

        return HealthCheckResult.Healthy("Outbox is draining.", data);
    }
}
