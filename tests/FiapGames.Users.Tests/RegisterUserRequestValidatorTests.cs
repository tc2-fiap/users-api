using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Application.Validators;

namespace FiapGames.Users.Tests;

public class RegisterUserRequestValidatorTests
{
    private readonly RegisterUserRequestValidator _validator = new();

    [Fact]
    public void Validate_WithValidRequest_Passes()
    {
        var result = _validator.Validate(new RegisterUserRequest("Jane Doe", "jane@example.com", "Password123"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "jane@example.com", "Password123")]
    [InlineData("Jane Doe", "not-an-email", "Password123")]
    [InlineData("Jane Doe", "jane@example.com", "short")]
    public void Validate_WithInvalidRequest_Fails(string name, string email, string password)
    {
        var result = _validator.Validate(new RegisterUserRequest(name, email, password));

        Assert.False(result.IsValid);
    }
}
