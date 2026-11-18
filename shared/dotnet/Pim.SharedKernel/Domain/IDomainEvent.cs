using MediatR;

namespace Pim.SharedKernel.Domain;

/// <summary>Marker for domain events, dispatched via MediatR after persistence.</summary>
public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredAtUtc { get; }
}
