using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Shared.Kernel.Repositories;
using FiapGames.Users.Api.Domain;

namespace FiapGames.Users.Api.Application.Abstractions;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken = default);

    Task<PagedResult<UserEvent>> GetAllEventsAdminAsync(PagedRequest request, string? eventType, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    Task AddEventAsync(UserEvent userEvent, CancellationToken cancellationToken = default);
}
