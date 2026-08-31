using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Shared.Kernel.Results;
using FiapGames.Users.Api.Application.Dtos;
using FiapGames.Users.Api.Domain;

namespace FiapGames.Users.Api.Application.Abstractions;

public interface IUserService
{
    Task<Result<UserResponse>> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<UserResponse>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<UserEventResponse>> GetAllUserEventsAdminAsync(PagedRequest request, string? eventType, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserResponse>> UpdateRoleAsync(Guid id, UserRole role, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
