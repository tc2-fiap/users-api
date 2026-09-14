using FiapGames.Shared.Infrastructure.Auth;

namespace FiapGames.Users.Tests;

public class InMemoryTokenRevocationStoreTests
{
    private readonly InMemoryTokenRevocationStore _sut = new();

    [Fact]
    public void IsRevoked_ForUnknownJti_ReturnsFalse()
    {
        Assert.False(_sut.IsRevoked("never-seen"));
    }

    [Fact]
    public void IsRevoked_AfterRevoke_ReturnsTrue()
    {
        _sut.Revoke("jti-1", DateTime.UtcNow.AddMinutes(30));

        Assert.True(_sut.IsRevoked("jti-1"));
    }

    [Fact]
    public void IsRevoked_AfterExpiry_ReturnsFalseAndSweepsEntry()
    {
        _sut.Revoke("jti-1", DateTime.UtcNow.AddMinutes(-1));

        Assert.False(_sut.IsRevoked("jti-1"));
    }
}
