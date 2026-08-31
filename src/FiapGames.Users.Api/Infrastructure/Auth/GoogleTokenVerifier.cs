using FiapGames.Users.Api.Application.Abstractions;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiapGames.Users.Api.Infrastructure.Auth;

public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly string? _clientId;
    private readonly ILogger<GoogleTokenVerifier> _logger;

    public GoogleTokenVerifier(IConfiguration configuration, ILogger<GoogleTokenVerifier> logger)
    {
        _clientId = configuration["Google:ClientId"];
        _logger = logger;
    }

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            _logger.LogWarning("Google sign-in attempted but Google:ClientId is not configured");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_clientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return new GoogleIdentity(payload.Subject, payload.Email, payload.Name);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Rejected an invalid Google ID token");
            return null;
        }
    }
}
