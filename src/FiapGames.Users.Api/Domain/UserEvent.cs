using FiapGames.Shared.Kernel.Entities;

namespace FiapGames.Users.Api.Domain;

// A system-wide audit trail — appended whenever this service publishes
// UserCreatedEvent, storing the actual payload (not a summary) so an admin
// can inspect what was really sent. Mirrors orders-api's OrderEvent.
public sealed class UserEvent : Entity
{
    public Guid UserId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredAtUtc { get; private set; }

    private UserEvent() { }

    public UserEvent(Guid userId, string eventType, string payload)
    {
        UserId = userId;
        EventType = eventType;
        Payload = payload;
        OccurredAtUtc = DateTime.UtcNow;
    }
}
