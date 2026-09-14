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

        var now = DateTime.UtcNow;
        if (user.IsLockedOut(now))
        {
            _logger.LogWarning("Login rejected for {Email}: account locked until {LockedUntilUtc}", request.Email, user.LockedUntilUtc);
            return Result.Failure<LoginResponse>(Error.Unauthorized("Too many failed attempts. Try again later."));
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(now);
            _repository.Update(user);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Login failed for {Email}: invalid credentials", request.Email);
            return Result.Failure<LoginResponse>(Error.Unauthorized("Invalid email or password."));
        }

        user.RegisterSuccessfulLogin();
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

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

    public async Task LogoutAsync(string jti, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        await _publishEndpoint.Publish(new TokenRevokedEvent(jti, expiresAtUtc), cancellationToken);

        _logger.LogInformation("Token {Jti} revoked via logout", jti);
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

    public async Task<PagedResult<UserResponse>> SearchUsersAdminAsync(PagedRequest request, string? name, CancellationToken cancellationToken = default)
    {
        var paged = await _repository.SearchAdminAsync(request, name, cancellationToken);
        var items = paged.Items.Select(UserResponse.FromDomain).ToList();
        return new PagedResult<UserResponse>(items, paged.TotalCount, paged.Page, paged.PageSize);
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

    public async Task<Result<UserResponse>> UpdateRoleAsync(Guid id, UserRole role, Guid callerId, CancellationToken cancellationToken = default)
    {
        // An Admin can't change their own role — the only way to get a
        // second Admin is for an existing one to promote someone else, so
        // allowing self-demotion could strand the system with no Admin left.
        if (id == callerId)
        {
            _logger.LogWarning("Role change rejected: user {UserId} attempted to change their own role", id);
            return Result.Failure<UserResponse>(Error.Validation("You cannot change your own role."));
        }

        var user = await _repository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return Result.Failure<UserResponse>(Error.NotFound($"User '{id}' was not found."));
        }

        var oldRole = user.Role;
        user.ChangeRole(role);
        _repository.Update(user);

        var roleChangedEvent = new RoleChangedEvent(user.Id, oldRole.ToString(), role.ToString(), callerId);
        await _repository.AddEventAsync(new UserEvent(user.Id, "RoleChangedEvent", JsonSerializer.Serialize(roleChangedEvent)), cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(roleChangedEvent, cancellationToken);

        _logger.LogInformation("User {UserId} role changed from {OldRole} to {NewRole} by {CallerId}", user.Id, oldRole, role, callerId);

        return Result.Success(UserResponse.FromDomain(user));
    }

    public async Task<Result> DeleteAsync(Guid id, Guid callerId, CancellationToken cancellationToken = default)
    {
        // Same reasoning as UpdateRoleAsync — an Admin deleting themself
        // could leave the system with no Admin account at all.
        if (id == callerId)
        {
            _logger.LogWarning("Delete rejected: user {UserId} attempted to delete their own account", id);
            return Result.Failure(Error.Validation("You cannot delete your own account."));
        }

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
