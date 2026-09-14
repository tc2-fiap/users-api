namespace FiapGames.Contracts;

// Fixed cross-service event contract — see instructions.md §8. Duplicated
// verbatim (namespace + shape) into every service that publishes or
// consumes it; see notes.md 21.
public sealed record RoleChangedEvent(Guid UserId, string OldRole, string NewRole, Guid ChangedByUserId);
