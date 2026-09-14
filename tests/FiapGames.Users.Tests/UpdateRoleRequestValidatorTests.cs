using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Application.Validators;
using FiapGames.Users.Api.Domain;

namespace FiapGames.Users.Tests;

public class UpdateRoleRequestValidatorTests
{
    private readonly UpdateRoleRequestValidator _validator = new();

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Player)]
    public void Validate_WithDefinedRole_Passes(UserRole role)
    {
        var result = _validator.Validate(new UpdateRoleRequest(role));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithUndefinedRole_Fails()
    {
        var result = _validator.Validate(new UpdateRoleRequest((UserRole)999));

        Assert.False(result.IsValid);
    }
}
