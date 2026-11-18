using Pim.SharedKernel.Domain;

namespace Pim.Catalog.Domain.Products;

public sealed record ProductCreatedDomainEvent(Guid ProductPublicId, string ProductCode) : IDomainEvent
{
    public DateTimeOffset OccurredAtUtc { get; } = DateTimeOffset.UtcNow;
}
