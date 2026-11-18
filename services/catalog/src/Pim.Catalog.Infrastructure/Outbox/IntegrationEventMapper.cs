using System.Text.Json;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Domain;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Translates a domain event into the outbox row that will carry it off this
/// service.
///
/// Static by design, and that is not laziness. <c>CatalogDbContext</c> is
/// constructed directly by the integration fixture, and R-12 recorded what
/// happens when behaviour a type needs is only wired up in the composition
/// root: the tests that new it up run unconfigured and fail as a group. Making
/// this static means enqueueing works for anyone holding a DbContext, whether
/// or not a container built it.
///
/// The mapping is an explicit switch rather than reflection or a naming
/// convention. A convention would map a newly added domain event silently and
/// wrongly; this throws, at the moment of the write, naming the type nobody
/// mapped. Losing an event quietly is the failure mode an outbox exists to
/// prevent, so it must not be reachable through carelessness.
/// </summary>
internal static class IntegrationEventMapper
{
    /// <summary>
    /// Wire vocabulary for lifecycle states. Deliberately the same strings the
    /// DDL stores, but declared separately: consumers depend on these, and a
    /// future change to how the column is persisted must not silently become a
    /// breaking change for them. <c>IntegrationEventMapperTests</c> asserts
    /// every enum member is present, so adding a state cannot slip through.
    /// </summary>
    private static readonly Dictionary<LifecycleState, string> LifecycleWireNames = new()
    {
        [LifecycleState.Planned] = "PLANNED",
        [LifecycleState.Active] = "ACTIVE",
        [LifecycleState.PhaseOut] = "PHASE_OUT",
        [LifecycleState.Discontinued] = "DISCONTINUED"
    };

    /// <summary>
    /// Maps one domain event. <paramref name="enqueuedAt"/> is passed in rather
    /// than read from the clock so that every message written by one
    /// SaveChanges shares a timestamp, and so the tests are not racing it.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The domain event has no wire contract. Deliberately fatal: the
    /// alternative is a transaction that commits a change nobody is told about.
    /// </exception>
    public static OutboxMessage ToOutboxMessage(
        IDomainEvent domainEvent,
        DateTimeOffset enqueuedAt,
        Guid? correlationId)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            ProductCreatedDomainEvent e => Build(
                IntegrationEventTypes.ProductCreated,
                schemaVersion: 1,
                AggregateTypes.Product,
                e.ProductPublicId,
                JsonSerializer.Serialize(
                    new ProductCreatedPayloadV1(e.ProductPublicId, e.ProductCode),
                    IntegrationEventJsonContext.Default.ProductCreatedPayloadV1),
                e.OccurredAtUtc,
                enqueuedAt,
                correlationId),

            ProductLifecycleChangedDomainEvent e => Build(
                IntegrationEventTypes.ProductLifecycleChanged,
                schemaVersion: 1,
                AggregateTypes.Product,
                e.ProductPublicId,
                JsonSerializer.Serialize(
                    new ProductLifecycleChangedPayloadV1(
                        e.ProductPublicId,
                        ToWire(e.PreviousState),
                        ToWire(e.NewState)),
                    IntegrationEventJsonContext.Default.ProductLifecycleChangedPayloadV1),
                e.OccurredAtUtc,
                enqueuedAt,
                correlationId),

            _ => throw new NotSupportedException(
                $"Domain event '{domainEvent.GetType().FullName}' has no integration event contract. " +
                "Add one to IntegrationEventMapper, or the change would commit with nothing published.")
        };
    }

    /// <summary>Exposed for the contract test that pins enum exhaustiveness.</summary>
    public static string ToWire(LifecycleState state) =>
        LifecycleWireNames.TryGetValue(state, out var wire)
            ? wire
            : throw new NotSupportedException(
                $"Lifecycle state '{state}' has no wire name. Consumers would receive an unmapped value.");

    private static OutboxMessage Build(
        string eventType,
        int schemaVersion,
        string aggregateType,
        Guid aggregateId,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset enqueuedAt,
        Guid? correlationId) =>
        OutboxMessage.Enqueue(
            // Generated here, once. A retry reuses the persisted id; nothing
            // downstream may regenerate it or consumer deduplication breaks.
            messageId: Guid.NewGuid(),
            occurredAt: occurredAt,
            enqueuedAt: enqueuedAt,
            eventType: eventType,
            schemaVersion: schemaVersion,
            aggregateType: aggregateType,
            aggregateId: aggregateId,
            payload: payload,
            correlationId: correlationId);
}
