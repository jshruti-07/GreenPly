using Pim.SharedKernel.Domain;

namespace Pim.Catalog.Domain.Products;

public sealed record ProductLifecycleChangedDomainEvent(
    Guid ProductPublicId,
    LifecycleState PreviousState,
    LifecycleState NewState) : IDomainEvent
{
    public DateTimeOffset OccurredAtUtc { get; } = DateTimeOffset.UtcNow;
}
