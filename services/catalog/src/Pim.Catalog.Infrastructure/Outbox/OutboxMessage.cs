namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// A message waiting to leave the service, written in the same transaction as
/// the aggregate change that produced it (ADR-007).
///
/// This is a persistence type, not a domain type. It deliberately lives in
/// Infrastructure: the Domain does not know that anything is published, and
/// making it know would put a delivery concern inside an invariant boundary.
///
/// It is mutable, unlike the aggregates, because the relay's whole job is to
/// move a row through a lifecycle. Transitions go through the methods below
/// rather than property setters so that the illegal ones — publishing a dead
/// message, completing one that was never claimed — cannot be written.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Statuses as stored. Strings, not an enum ordinal: the value is
    /// read by operators in SQL, and an ordinal would make that guesswork.</summary>
    public static class Statuses
    {
        public const string Pending = "pending";
        public const string Publishing = "publishing";
        public const string Published = "published";
        public const string Dead = "dead";
    }

    private OutboxMessage()
    {
        // EF Core materialisation. Required-member initialisation happens
        // through the tracked properties, not this constructor.
        EventType = string.Empty;
        AggregateType = string.Empty;
        Payload = string.Empty;
        Status = Statuses.Pending;
    }

    public long Id { get; private set; }

    /// <summary>
    /// The consumer's idempotency key. Generated once, at enqueue, and never
    /// regenerated — a retry must carry the SAME id or at-least-once delivery
    /// becomes at-least-once duplication that consumers cannot detect.
    /// </summary>
    public Guid MessageId { get; private set; }

    /// <summary>When the domain event occurred, not when it was enqueued.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset EnqueuedAt { get; private set; }

    /// <summary>Wire contract identifier, e.g. <c>catalog.product.created</c>.</summary>
    public string EventType { get; private set; }

    public int SchemaVersion { get; private set; }

    public string AggregateType { get; private set; }

    /// <summary>
    /// The aggregate's PUBLIC id. Never the bigint surrogate: ADR-004 rule 2
    /// makes cross-service references logical, and the surrogate is private.
    /// </summary>
    public Guid AggregateId { get; private set; }

    /// <summary>Serialized event body. Stored as jsonb.</summary>
    public string Payload { get; private set; }

    public Guid? CorrelationId { get; private set; }

    public string Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public string? LeasedBy { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Creates a message ready to be added to the DbContext. Nothing is
    /// published here; the row is the publication intent, and the aggregate's
    /// transaction is what makes that intent durable.
    /// </summary>
    public static OutboxMessage Enqueue(
        Guid messageId,
        DateTimeOffset occurredAt,
        DateTimeOffset enqueuedAt,
        string eventType,
        int schemaVersion,
        string aggregateType,
        Guid aggregateId,
        string payload,
        Guid? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message id must not be empty.", nameof(messageId));
        }

        if (aggregateId == Guid.Empty)
        {
            throw new ArgumentException("Aggregate id must not be empty.", nameof(aggregateId));
        }

        return new OutboxMessage
        {
            MessageId = messageId,
            OccurredAt = occurredAt,
            EnqueuedAt = enqueuedAt,
            EventType = eventType,
            SchemaVersion = schemaVersion,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = payload,
            CorrelationId = correlationId,
            Status = Statuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = enqueuedAt
        };
    }
}
