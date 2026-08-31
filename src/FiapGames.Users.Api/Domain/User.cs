using FiapGames.Shared.Kernel.Entities;

namespace FiapGames.Users.Api.Domain;

public class User : Entity
{
    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    // Null for a Google-only account — see CreateFromGoogle. LoginAsync
    // must reject password login for these with a clear error, not a
    // hash-mismatch.
    public string? PasswordHash { get; private set; }

    // Stable Google "sub" claim, set once a Google sign-in has occurred
    // (either at account creation or via LinkGoogleAccount for an
    // existing password account with a matching email).
    public string? GoogleSubjectId { get; private set; }

    public UserRole Role { get; private set; }

    private User() { }

    public User(string name, string email, string passwordHash, UserRole role = UserRole.Player)
    {
        Name = name;
        Email = email.ToLowerInvariant();
        PasswordHash = passwordHash;
        Role = role;
    }

    private User(string name, string email, string googleSubjectId)
    {
        Name = name;
        Email = email.ToLowerInvariant();
        PasswordHash = null;
        GoogleSubjectId = googleSubjectId;
        Role = UserRole.Player;
    }

    public static User CreateFromGoogle(string name, string email, string googleSubjectId) =>
        new(name, email, googleSubjectId);

    public void LinkGoogleAccount(string googleSubjectId)
    {
        GoogleSubjectId = googleSubjectId;
        Touch();
    }

    public void UpdateProfile(string name, string email)
    {
        Name = name;
        Email = email.ToLowerInvariant();
        Touch();
    }

    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        Touch();
    }

    public void ChangeRole(UserRole role)
    {
        Role = role;
        Touch();
    }
}
