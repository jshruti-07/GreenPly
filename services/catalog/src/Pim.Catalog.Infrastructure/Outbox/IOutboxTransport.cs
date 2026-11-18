namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Where a claimed message goes. The relay owns claiming, ordering, retry and
/// dead-lettering; a transport owns exactly one thing — putting bytes on a wire
/// — and is the only part of this that knows a broker exists.
///
/// The seam is here so that the outbox, which is the load-bearing and
/// hard-to-get-right half, is testable and shippable without a broker client.
/// See ADR-007 for why the RabbitMQ implementation is not in this sprint.
/// </summary>
public interface IOutboxTransport
{
    /// <summary>
    /// Human-readable name of the destination, for logs and the health check.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this transport actually delivers to another process. False for
    /// the development transport, which lets the health check and the sprint
    /// notes state plainly that nothing is leaving the service — rather than
    /// showing a healthy, draining queue and implying that it is.
    /// </summary>
    bool IsDeliveringExternally { get; }

    /// <summary>
    /// Publishes one message. Must be idempotent from the caller's point of
    /// view only in the sense that redelivery is expected: the relay may hand
    /// over the same message again after a lease lapse or a crash, and
    /// consumers deduplicate on <see cref="OutboxClaim.MessageId"/>.
    ///
    /// Throwing marks the attempt failed and schedules a retry. Returning
    /// normally is taken as "the broker has accepted responsibility for this
    /// message" — an implementation that returns before the broker has
    /// confirmed will lose messages the outbox believes were delivered.
    /// </summary>
    Task PublishAsync(OutboxClaim message, CancellationToken cancellationToken);
}
