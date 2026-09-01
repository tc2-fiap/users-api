using FiapGames.Contracts;
using FiapGames.Shared.Infrastructure.Auth;
using FiapGames.Shared.Infrastructure.Settings;
using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Shared.Kernel.Results;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Application.Services;
using FiapGames.Users.Api.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FiapGames.Users.Tests;

public class UserServiceTests
{
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IGoogleTokenVerifier _googleTokenVerifier = Substitute.For<IGoogleTokenVerifier>();
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var jwtSettings = Options.Create(new JwtSettings { ExpiryMinutes = 60 });
        var logger = Substitute.For<ILogger<UserService>>();
        _sut = new UserService(_repository, _passwordHasher, _tokenService, _googleTokenVerifier, _publishEndpoint, jwtSettings, logger);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailIsNew_CreatesUserAndPublishesEvent()
    {
        _repository.GetByEmailAsync("new@example.com").Returns((User?)null);
        _passwordHasher.Hash("Password123").Returns("hashed-password");

        var result = await _sut.RegisterAsync(new RegisterUserRequest("Jane Doe", "new@example.com", "Password123"));

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.com", result.Value.Email);
        await _repository.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repository.Received(1).AddEventAsync(Arg.Any<UserEvent>(), Arg.Any<CancellationToken>());
        await _publishEndpoint.Received(1).Publish(Arg.Any<UserCreatedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ReturnsConflict()
    {
        var existing = new User("Existing", "taken@example.com", "hash");
        _repository.GetByEmailAsync("taken@example.com").Returns(existing);

        var result = await _sut.RegisterAsync(new RegisterUserRequest("Jane Doe", "taken@example.com", "Password123"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        var user = new User("Jane Doe", "jane@example.com", "hashed-password");
        _repository.GetByEmailAsync("jane@example.com").Returns(user);
        _passwordHasher.Verify("Password123", "hashed-password").Returns(true);
        _tokenService.GenerateToken(user.Id, user.Email, user.Role.ToString()).Returns("jwt-token");

        var result = await _sut.LoginAsync(new LoginRequest("jane@example.com", "Password123"));

        Assert.True(result.IsSuccess);
        Assert.Equal("jwt-token", result.Value.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsUnauthorized()
    {
        var user = new User("Jane Doe", "jane@example.com", "hashed-password");
        _repository.GetByEmailAsync("jane@example.com").Returns(user);
        _passwordHasher.Verify("WrongPassword", "hashed-password").Returns(false);

        var result = await _sut.LoginAsync(new LoginRequest("jane@example.com", "WrongPassword"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsGoogleOnly_ReturnsUnauthorizedWithoutHashComparison()
    {
        var user = User.CreateFromGoogle("Jane Doe", "jane@example.com", "google-sub-1");
        _repository.GetByEmailAsync("jane@example.com").Returns(user);

        var result = await _sut.LoginAsync(new LoginRequest("jane@example.com", "AnyPassword123"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        _passwordHasher.DidNotReceiveWithAnyArgs().Verify(default!, default!);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_WhenTokenIsInvalid_ReturnsUnauthorized()
    {
        _googleTokenVerifier.VerifyAsync("bad-token", Arg.Any<CancellationToken>()).Returns((GoogleIdentity?)null);

        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginRequest("bad-token"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_WhenNoAccountExists_CreatesOneAndPublishesEvent()
    {
        var identity = new GoogleIdentity("google-sub-1", "new@example.com", "New User");
        _googleTokenVerifier.VerifyAsync("good-token", Arg.Any<CancellationToken>()).Returns(identity);
        _repository.GetByGoogleSubjectIdAsync("google-sub-1", Arg.Any<CancellationToken>()).Returns((User?)null);
        _repository.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);
        _tokenService.GenerateToken(Arg.Any<Guid>(), "new@example.com", "Player").Returns("jwt-token");

        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginRequest("good-token"));

        Assert.True(result.IsSuccess);
        await _repository.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).AddEventAsync(Arg.Any<UserEvent>(), Arg.Any<CancellationToken>());
        await _publishEndpoint.Received(1).Publish(Arg.Any<UserCreatedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginWithGoogleAsync_WhenEmailMatchesExistingPasswordAccount_AutoLinksInsteadOfDuplicating()
    {
        var existing = new User("Jane Doe", "jane@example.com", "hashed-password");
        var identity = new GoogleIdentity("google-sub-1", "jane@example.com", "Jane Doe");
        _googleTokenVerifier.VerifyAsync("good-token", Arg.Any<CancellationToken>()).Returns(identity);
        _repository.GetByGoogleSubjectIdAsync("google-sub-1", Arg.Any<CancellationToken>()).Returns((User?)null);
        _repository.GetByEmailAsync("jane@example.com", Arg.Any<CancellationToken>()).Returns(existing);
        _tokenService.GenerateToken(existing.Id, existing.Email, "Player").Returns("jwt-token");

        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginRequest("good-token"));

        Assert.True(result.IsSuccess);
        await _repository.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        _repository.Received(1).Update(existing);
        Assert.Equal("google-sub-1", existing.GoogleSubjectId);
    }

    [Fact]
    public async Task UpdateRoleAsync_WhenUserExists_ChangesRole()
    {
        var user = new User("Jane Doe", "jane@example.com", "hash");
        _repository.GetByIdAsync(user.Id).Returns(user);

        var result = await _sut.UpdateRoleAsync(user.Id, UserRole.Admin);

        Assert.True(result.IsSuccess);
        Assert.Equal("Admin", result.Value.Role);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserDoesNotExist_ReturnsNotFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>()).Returns((User?)null);

        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task UpdateAsync_WhenEmailBelongsToAnotherUser_ReturnsConflict()
    {
        var user = new User("Jane Doe", "jane@example.com", "hash");
        var otherUser = new User("Other", "other@example.com", "hash");
        _repository.GetByIdAsync(user.Id).Returns(user);
        _repository.GetByEmailAsync("other@example.com").Returns(otherUser);

        var result = await _sut.UpdateAsync(user.Id, new UpdateUserRequest("Jane Doe", "other@example.com"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task DeleteAsync_WhenUserExists_RemovesUser()
    {
        var user = new User("Jane Doe", "jane@example.com", "hash");
        _repository.GetByIdAsync(user.Id).Returns(user);

        var result = await _sut.DeleteAsync(user.Id);

        Assert.True(result.IsSuccess);
        _repository.Received(1).Remove(user);
    }

    [Fact]
    public async Task GetPagedAsync_MapsDomainUsersToResponses()
    {
        var user = new User("Jane Doe", "jane@example.com", "hash");
        var pagedRequest = new PagedRequest { Page = 1, PageSize = 10 };
        _repository.GetPagedAsync(Arg.Any<PagedRequest>())
            .Returns(new PagedResult<User>([user], 1, 1, 10));

        var result = await _sut.GetPagedAsync(pagedRequest);

        Assert.Single(result.Items);
        Assert.Equal(user.Email, result.Items.First().Email);
    }

    [Fact]
    public async Task GetAllUserEventsAdminAsync_ReturnsEventsAcrossUsers()
    {
        var eventA = new UserEvent(Guid.NewGuid(), "UserCreatedEvent", "{}");
        var eventB = new UserEvent(Guid.NewGuid(), "UserCreatedEvent", "{}");
        _repository.GetAllEventsAdminAsync(Arg.Any<PagedRequest>(), null, null, null, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<UserEvent>([eventA, eventB], 2, 1, 10));

        var result = await _sut.GetAllUserEventsAdminAsync(new PagedRequest(), null, null, null);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task SearchUsersAdminAsync_ForwardsNameFilterAndMapsResults()
    {
        var user = new User("Jane Doe", "jane@example.com", "hashed-password");
        _repository.SearchAdminAsync(Arg.Any<PagedRequest>(), "Jane", Arg.Any<CancellationToken>())
            .Returns(new PagedResult<User>([user], 1, 1, 10));

        var result = await _sut.SearchUsersAdminAsync(new PagedRequest(), "Jane");

        Assert.Single(result.Items);
        Assert.Equal(user.Email, result.Items.First().Email);
        await _repository.Received(1).SearchAdminAsync(Arg.Any<PagedRequest>(), "Jane", Arg.Any<CancellationToken>());
    }
}
