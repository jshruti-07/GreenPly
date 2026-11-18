using System.Text.Json;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Outbox;
using Pim.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests.Outbox;

/// <summary>
/// Pins the published wire contract. These need no database — they live in this
/// assembly because the mapper is internal to Infrastructure and only this
/// project can see it.
///
/// What is being protected here is other people's deserializers. Every literal
/// below is a value some future consumer will hard-code, so a test failing here
/// means "this is a breaking change", not "update the expected string".
/// </summary>
public sealed class IntegrationEventMapperTests
{
    private static readonly DateTimeOffset EnqueuedAt =
        new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ProductCreated_MapsToTheDocumentedContract()
    {
        var publicId = Guid.NewGuid();
        var domainEvent = new ProductCreatedDomainEvent(publicId, "FD-90");

        var message = IntegrationEventMapper.ToOutboxMessage(domainEvent, EnqueuedAt, correlationId: null);

        message.EventType.ShouldBe("catalog.product.created");
        message.SchemaVersion.ShouldBe(1);
        message.AggregateType.ShouldBe("product");
        message.AggregateId.ShouldBe(publicId);
        message.Status.ShouldBe(OutboxMessage.Statuses.Pending);
        message.AttemptCount.ShouldBe(0);
        message.EnqueuedAt.ShouldBe(EnqueuedAt);

        // A message must be retryable from the moment it is written; a future
        // next_attempt_at would silently delay the very first delivery.
        message.NextAttemptAt.ShouldBe(EnqueuedAt);

        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.GetProperty("productId").GetGuid().ShouldBe(publicId);
        payload.RootElement.GetProperty("productCode").GetString().ShouldBe("FD-90");
    }

    [Fact]
    public void ProductLifecycleChanged_UsesWireStates_NotEnumNames()
    {
        var publicId = Guid.NewGuid();
        var domainEvent = new ProductLifecycleChangedDomainEvent(
            publicId, LifecycleState.Active, LifecycleState.PhaseOut);

        var message = IntegrationEventMapper.ToOutboxMessage(domainEvent, EnqueuedAt, correlationId: null);

        message.EventType.ShouldBe("catalog.product.lifecycle-changed");

        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.GetProperty("previousState").GetString().ShouldBe("ACTIVE");

        // Not "PhaseOut". The CLR name is an internal detail; PHASE_OUT is the
        // published value and matches the vocabulary in the DDL's CHECK.
        payload.RootElement.GetProperty("newState").GetString().ShouldBe("PHASE_OUT");
    }

    [Fact]
    public void OccurredAt_ComesFromTheEvent_NotFromEnqueueTime()
    {
        var domainEvent = new ProductCreatedDomainEvent(Guid.NewGuid(), "FD-90");

        var message = IntegrationEventMapper.ToOutboxMessage(domainEvent, EnqueuedAt, correlationId: null);

        // The gap between these two is the only measure of publish lag there
        // is. Collapsing them would make the outbox look instantaneous forever.
        message.OccurredAt.ShouldBe(domainEvent.OccurredAtUtc);
        message.OccurredAt.ShouldNotBe(message.EnqueuedAt);
    }

    [Fact]
    public void EveryMessageGetsADistinctIdempotencyKey()
    {
        var domainEvent = new ProductCreatedDomainEvent(Guid.NewGuid(), "FD-90");

        var first = IntegrationEventMapper.ToOutboxMessage(domainEvent, EnqueuedAt, correlationId: null);
        var second = IntegrationEventMapper.ToOutboxMessage(domainEvent, EnqueuedAt, correlationId: null);

        first.MessageId.ShouldNotBe(Guid.Empty);
        first.MessageId.ShouldNotBe(second.MessageId);
    }

    [Fact]
    public void UnmappedDomainEvent_Throws_RatherThanCommittingSilently()
    {
        var unmapped = new UnmappedTestDomainEvent();

        var ex = Should.Throw<NotSupportedException>(
            () => IntegrationEventMapper.ToOutboxMessage(unmapped, EnqueuedAt, correlationId: null));

        // The message has to name the type, because the person reading it is
        // looking at a failed write and needs to know which event caused it.
        ex.Message.ShouldContain(nameof(UnmappedTestDomainEvent));
    }

    [Theory]
    [InlineData(LifecycleState.Planned, "PLANNED")]
    [InlineData(LifecycleState.Active, "ACTIVE")]
    [InlineData(LifecycleState.PhaseOut, "PHASE_OUT")]
    [InlineData(LifecycleState.Discontinued, "DISCONTINUED")]
    public void LifecycleStates_HaveTheirPublishedNames(LifecycleState state, string expected) =>
        IntegrationEventMapper.ToWire(state).ShouldBe(expected);

    [Fact]
    public void EveryLifecycleState_HasAWireName()
    {
        // Exhaustiveness, not a list of cases. Adding a state to the enum
        // without a wire name fails here rather than at the first publish
        // attempt in whatever environment reaches it first.
        foreach (var state in Enum.GetValues<LifecycleState>())
        {
            Should.NotThrow(() => IntegrationEventMapper.ToWire(state));
        }
    }

    private sealed record UnmappedTestDomainEvent : IDomainEvent
    {
        public DateTimeOffset OccurredAtUtc { get; } = DateTimeOffset.UtcNow;
    }
}
