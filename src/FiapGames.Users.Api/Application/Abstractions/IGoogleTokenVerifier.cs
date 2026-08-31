namespace FiapGames.Users.Api.Application.Abstractions;

public sealed record GoogleIdentity(string Subject, string Email, string Name);

// Kept as an interface — like ICatalogClient in orders-api — so UserService
// stays unit-testable without a real call to Google.
public interface IGoogleTokenVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}
