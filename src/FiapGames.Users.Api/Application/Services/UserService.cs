using System.Text.Json;
using FiapGames.Contracts;
using FiapGames.Shared.Infrastructure.Auth;
using FiapGames.Shared.Infrastructure.Settings;
using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Shared.Kernel.Results;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiapGames.Users.Api.Application.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository repository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IGoogleTokenVerifier googleTokenVerifier,
        IPublishEndpoint publishEndpoint,
        IOptions<JwtSettings> jwtSettings,
        ILogger<UserService> logger)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _googleTokenVerifier = googleTokenVerifier;
        _publishEndpoint = publishEndpoint;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<Result<UserResponse>> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            _logger.LogWarning("User registration rejected for {Email}: email already registered", request.Email);
            return Result.Failure<UserResponse>(Error.Conflict("A user with this email already exists."));
        }

        var user = new User(request.Name, request.Email, _passwordHasher.Hash(request.Password));

        await _repository.AddAsync(user, cancellationToken);

        var userCreatedEvent = new UserCreatedEvent(user.Id, user.Name, user.Email);
        await _repository.AddEventAsync(new UserEvent(user.Id, "UserCreatedEvent", JsonSerializer.Serialize(userCreatedEvent)), cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(userCreatedEvent, cancellationToken);

        _logger.LogInformation("User {UserId} registered with email {Email}", user.Id, user.Email);

        return Result.Success(UserResponse.FromDomain(user));
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("Login failed for {Email}: invalid credentials", request.Email);
            return Result.Failure<LoginResponse>(Error.Unauthorized("Invalid email or password."));
        }

        if (user.PasswordHash is null)
        {
            _logger.LogWarning("Login failed for {Email}: account uses Google sign-in, has no password", request.Email);
            return Result.Failure<LoginResponse>(Error.Unauthorized("This account signs in with Google."));
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for {Email}: invalid credentials", request.Email);
            return Result.Failure<LoginResponse>(Error.Unauthorized("Invalid email or password."));
        }

        var token = _tokenService.GenerateToken(user.Id, user.Email, user.Role.ToString());

        _logger.LogInformation("User {UserId} logged in", user.Id);

        return Result.Success(new LoginResponse(token, DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes)));
    }

    public async Task<Result<LoginResponse>> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
    {
        var identity = await _googleTokenVerifier.VerifyAsync(request.IdToken, cancellationToken);
        if (identity is null)
        {
            _logger.LogWarning("Google sign-in rejected: invalid or unverifiable ID token");
            return Result.Failure<LoginResponse>(Error.Unauthorized("Invalid Google sign-in."));
        }

        var user = await _repository.GetByGoogleSubjectIdAsync(identity.Subject, cancellationToken);

        if (user is null)
        {
            // Auto-link: Google verifies emails itself, so a matching email
            // on an existing password account is treated as the same
            // person rather than creating a second account.
            user = await _repository.GetByEmailAsync(identity.Email, cancellationToken);
            if (user is not null)
            {
                user.LinkGoogleAccount(identity.Subject);
                _repository.Update(user);
                _logger.LogInformation("Linked Google account to existing user {UserId}", user.Id);
            }
            else
            {
                user = User.CreateFromGoogle(identity.Name, identity.Email, identity.Subject);
                await _repository.AddAsync(user, cancellationToken);

                var userCreatedEvent = new UserCreatedEvent(user.Id, user.Name, user.Email);
                await _repository.AddEventAsync(new UserEvent(user.Id, "UserCreatedEvent", JsonSerializer.Serialize(userCreatedEvent)), cancellationToken);
                await _publishEndpoint.Publish(userCreatedEvent, cancellationToken);

                _logger.LogInformation("User {UserId} created via Google sign-in with email {Email}", user.Id, user.Email);
            }

            await _repository.SaveChangesAsync(cancellationToken);
        }

        var token = _tokenService.GenerateToken(user.Id, user.Email, user.Role.ToString());

        _logger.LogInformation("User {UserId} logged in via Google", user.Id);

        return Result.Success(new LoginResponse(token, DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes)));
    }

    public async Task<Result<UserResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return Result.Failure<UserResponse>(Error.NotFound($"User '{id}' was not found."));
        }

        return Result.Success(UserResponse.FromDomain(user));
    }

    public async Task<PagedResult<UserResponse>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var paged = await _repository.GetPagedAsync(request, cancellationToken);
        var items = paged.Items.Select(UserResponse.FromDomain).ToList();
        return new PagedResult<UserResponse>(items, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<PagedResult<UserEventResponse>> GetAllUserEventsAdminAsync(PagedRequest request, string? eventType, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var paged = await _repository.GetAllEventsAdminAsync(request, eventType, from, to, cancellationToken);
        var items = paged.Items.Select(UserEventResponse.FromDomain).ToList();
        return new PagedResult<UserEventResponse>(items, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return Result.Failure<UserResponse>(Error.NotFound($"User '{id}' was not found."));
        }

        var emailOwner = await _repository.GetByEmailAsync(request.Email, cancellationToken);
        if (emailOwner is not null && emailOwner.Id != id)
        {
            _logger.LogWarning("Update rejected for user {UserId}: email {Email} already belongs to another account", id, request.Email);
            return Result.Failure<UserResponse>(Error.Conflict("A user with this email already exists."));
        }

        user.UpdateProfile(request.Name, request.Email);
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} updated", user.Id);

        return Result.Success(UserResponse.FromDomain(user));
    }

    public async Task<Result<UserResponse>> UpdateRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return Result.Failure<UserResponse>(Error.NotFound($"User '{id}' was not found."));
        }

        user.ChangeRole(role);
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} role changed to {Role}", user.Id, role);

        return Result.Success(UserResponse.FromDomain(user));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return Result.Failure(Error.NotFound($"User '{id}' was not found."));
        }

        _repository.Remove(user);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} deleted", id);

        return Result.Success();
    }
}
