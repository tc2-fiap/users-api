using FiapGames.Users.Api.Application.Dtos;
using FluentValidation;

namespace FiapGames.Users.Api.Application.Validators;

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    // Values people actually pick, sometimes still passing a naive length-only rule.
    // Checked case-insensitively against the raw password, not a substring match.
    private static readonly HashSet<string> CommonWeakPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "password1234",
        "12345678", "123456789", "1234567890",
        "qwertyuiop", "qwerty123", "qwerty1234",
        "letmein123", "welcome123", "admin1234", "iloveyou1",
        "abc123456", "changeme123", "trustno1!",
    };

    public RegisterUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12).WithMessage("Password must be at least 12 characters long.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.")
            .Must(password => !CommonWeakPasswords.Contains(password))
                .WithMessage("Password is too common; choose a less predictable one.");
    }
}
