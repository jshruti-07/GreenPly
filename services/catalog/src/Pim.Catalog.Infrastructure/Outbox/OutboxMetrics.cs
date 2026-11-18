using System.Diagnostics.Metrics;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Relay instrumentation, on <see cref="System.Diagnostics.Metrics"/> — part of
/// the BCL, so this adds no dependency and no licensing question, and it is the
/// interface an OpenTelemetry exporter reads when that lands.
///
/// The counters chosen are the ones that answer an operator's actual questions:
/// is anything moving, is anything failing, and has anything been given up on.
/// Dead-lettering gets its own counter rather than a tag on failures because it
/// is the only one that should ever page someone.
/// </summary>
public sealed class OutboxMetrics : IDisposable
{
    public const string MeterName = "Pim.Catalog.Outbox";

    private readonly Meter _meter;
    private readonly Counter<long> _published;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _reclaimed;
    private readonly Histogram<double> _publishDurationMs;

    public OutboxMetrics()
    {
        _meter = new Meter(MeterName);

        _published = _meter.CreateCounter<long>(
            "pim.outbox.published", unit: "{message}", description: "Messages accepted by the transport.");

        _failed = _meter.CreateCounter<long>(
            "pim.outbox.failed", unit: "{message}", description: "Publish attempts that threw and will be retried.");

        _deadLettered = _meter.CreateCounter<long>(
            "pim.outbox.dead_lettered", unit: "{message}", description: "Messages abandoned after exhausting attempts.");

        _reclaimed = _meter.CreateCounter<long>(
            "pim.outbox.reclaimed", unit: "{message}", description: "Messages returned to the queue after a lease lapsed.");

        _publishDurationMs = _meter.CreateHistogram<double>(
            "pim.outbox.publish.duration", unit: "ms", description: "Time in the transport call.");
    }

    public void RecordPublished(string eventType, double elapsedMs)
    {
        _published.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
        _publishDurationMs.Record(elapsedMs, new KeyValuePair<string, object?>("event.type", eventType));
    }

    public void RecordFailed(string eventType) =>
        _failed.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public void RecordDeadLettered(string eventType) =>
        _deadLettered.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public void RecordReclaimed(int count)
    {
        if (count > 0)
        {
            _reclaimed.Add(count);
        }
    }

    public void Dispose() => _meter.Dispose();
}
