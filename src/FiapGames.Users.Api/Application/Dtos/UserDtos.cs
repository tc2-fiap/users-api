namespace FiapGames.Users.Api.Application.Dtos;

public sealed record RegisterUserRequest(string Name, string Email, string Password);

public sealed record UpdateUserRequest(string Name, string Email);

public sealed record LoginRequest(string Email, string Password);

public sealed record GoogleLoginRequest(string IdToken);

public sealed record UpdateRoleRequest(Domain.UserRole Role);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

public sealed record UserResponse(Guid Id, string Name, string Email, string Role, DateTime CreatedAtUtc)
{
    public static UserResponse FromDomain(Domain.User user) =>
        new(user.Id, user.Name, user.Email, user.Role.ToString(), user.CreatedAtUtc);
}

public sealed record UserEventResponse(Guid Id, string EventType, string Payload, DateTime OccurredAtUtc)
{
    public static UserEventResponse FromDomain(Domain.UserEvent userEvent) =>
        new(userEvent.Id, userEvent.EventType, userEvent.Payload, userEvent.OccurredAtUtc);
}
