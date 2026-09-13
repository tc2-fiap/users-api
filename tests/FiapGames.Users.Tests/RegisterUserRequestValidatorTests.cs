using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Application.Validators;

namespace FiapGames.Users.Tests;

public class RegisterUserRequestValidatorTests
{
    private readonly RegisterUserRequestValidator _validator = new();

    [Fact]
    public void Validate_WithValidRequest_Passes()
    {
        var result = _validator.Validate(new RegisterUserRequest("Jane Doe", "jane@example.com", "Passw0rd!2345"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "jane@example.com", "Passw0rd!2345")]
    [InlineData("Jane Doe", "not-an-email", "Passw0rd!2345")]
    [InlineData("Jane Doe", "jane@example.com", "short")]
    [InlineData("Jane Doe", "jane@example.com", "nouppercase1!")]
    [InlineData("Jane Doe", "jane@example.com", "NOLOWERCASE1!")]
    [InlineData("Jane Doe", "jane@example.com", "NoDigitsHere!")]
    [InlineData("Jane Doe", "jane@example.com", "NoSpecialChar123")]
    [InlineData("Jane Doe", "jane@example.com", "Password123")]
    public void Validate_WithInvalidRequest_Fails(string name, string email, string password)
    {
        var result = _validator.Validate(new RegisterUserRequest(name, email, password));

        Assert.False(result.IsValid);
    }
}
