using FiapGames.Users.Api.Application.Dtos;
using FluentValidation;

namespace FiapGames.Users.Api.Application.Validators;

public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
    }
}
