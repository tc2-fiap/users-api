using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Application.Validators;

namespace FiapGames.Users.Tests;

public class GoogleLoginRequestValidatorTests
{
    private readonly GoogleLoginRequestValidator _validator = new();

    [Fact]
    public void Validate_WithIdToken_Passes()
    {
        var result = _validator.Validate(new GoogleLoginRequest("some-id-token"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyIdToken_Fails(string idToken)
    {
        var result = _validator.Validate(new GoogleLoginRequest(idToken));

        Assert.False(result.IsValid);
    }
}
