using Microsoft.Extensions.Logging;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Development transport. Writes each message to the log and reports success.
///
/// This is NOT a stand-in for a broker and must never be registered where
/// delivery is expected. It exists so the outbox itself — the transactional
/// enqueue, ordering, leasing, retry and dead-lettering — can be exercised
/// end to end while the RabbitMQ client package remains unvalidated
/// (ADR-007, §Deferred).
///
/// <see cref="IsDeliveringExternally"/> is false, which is what makes the
/// health check say so out loud. A drained queue with nothing on the other end
/// looks exactly like a working system, and that is the reading this flag is
/// here to prevent.
/// </summary>
public sealed partial class LoggingOutboxTransport(ILogger<LoggingOutboxTransport> logger) : IOutboxTransport
{
    public string Description => "log-only (development; no broker attached)";

    public bool IsDeliveringExternally => false;

    public Task PublishAsync(OutboxClaim message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogPublished(
            logger,
            message.EventType,
            message.SchemaVersion,
            message.MessageId,
            message.AggregateType,
            message.AggregateId,
            message.Payload);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Information,
        Message = "Outbox (log-only transport) {EventType} v{SchemaVersion} message {MessageId} " +
                  "for {AggregateType} {AggregateId}: {Payload}")]
    private static partial void LogPublished(
        ILogger logger,
        string eventType,
        int schemaVersion,
        Guid messageId,
        string aggregateType,
        Guid aggregateId,
        string payload);
}
