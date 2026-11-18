using System.Text.Json.Serialization;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// The wire contracts catalog publishes. These are NOT the domain events.
///
/// The separation is the point. A domain event is an internal fact shaped for
/// this service's own model and free to change with it; an integration event is
/// a published API that another team's deserializer depends on. Serializing the
/// domain record directly would make every domain refactor a breaking change
/// for consumers, discovered by them rather than by us.
///
/// Contract rules, which the tests pin:
///   - <c>event_type</c> and <c>schema_version</c> together identify the shape.
///   - Fields are added, never removed or retyped, within a schema version.
///   - Ids on the wire are public ids. The bigint surrogate never leaves.
/// </summary>
internal static class IntegrationEventTypes
{
    public const string ProductCreated = "catalog.product.created";
    public const string ProductLifecycleChanged = "catalog.product.lifecycle-changed";
}

/// <summary>Aggregate type names as they appear on the wire.</summary>
internal static class AggregateTypes
{
    public const string Product = "product";
}

/// <summary><c>catalog.product.created</c> v1.</summary>
internal sealed record ProductCreatedPayloadV1(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("productCode")] string ProductCode);

/// <summary><c>catalog.product.lifecycle-changed</c> v1.</summary>
internal sealed record ProductLifecycleChangedPayloadV1(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("previousState")] string PreviousState,
    [property: JsonPropertyName("newState")] string NewState);

/// <summary>
/// Source-generated serialization for the payloads above.
///
/// Source generation rather than reflection because the payload set is closed
/// and known at compile time, and because a reflection-based serializer would
/// silently start emitting any property later added to these records. With a
/// generated context the contract is compiled, not discovered.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ProductCreatedPayloadV1))]
[JsonSerializable(typeof(ProductLifecycleChangedPayloadV1))]
internal sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
