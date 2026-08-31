using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiapGames.Users.Api.Infrastructure.Persistence;

public sealed class UserRepository : IUserRepository
{
    private readonly UsersDbContext _context;

    public UserRepository(UsersDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.ToLowerInvariant();
        return _context.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public Task<User?> GetByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken = default) =>
        _context.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == googleSubjectId, cancellationToken);

    public async Task<PagedResult<User>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Users.OrderBy(u => u.CreatedAtUtc);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query.Skip(request.Skip).Take(request.PageSize ?? 10).ToListAsync(cancellationToken);

        return new PagedResult<User>(items, totalCount, request.Page ?? 1, request.PageSize ?? 10);
    }

    public async Task<PagedResult<UserEvent>> GetAllEventsAdminAsync(PagedRequest request, string? eventType, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var query = _context.UserEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);
        if (from.HasValue)
            query = query.Where(e => e.OccurredAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.OccurredAtUtc <= to.Value);

        query = query.OrderByDescending(e => e.OccurredAtUtc);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query.Skip(request.Skip).Take(request.PageSize ?? 10).ToListAsync(cancellationToken);

        return new PagedResult<UserEvent>(items, totalCount, request.Page ?? 1, request.PageSize ?? 10);
    }

    public Task AddEventAsync(UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        _context.UserEvents.Add(userEvent);
        return Task.CompletedTask;
    }

    public Task AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(User entity) => _context.Users.Update(entity);

    public void Remove(User entity) => _context.Users.Remove(entity);

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken) >= 0;
}
