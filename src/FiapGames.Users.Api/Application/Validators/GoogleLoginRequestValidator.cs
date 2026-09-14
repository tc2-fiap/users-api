using FiapGames.Users.Api.Application.Dtos;
using FluentValidation;

namespace FiapGames.Users.Api.Application.Validators;

public sealed class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty();
    }
}
