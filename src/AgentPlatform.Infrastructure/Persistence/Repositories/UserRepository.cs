using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for user aggregates.
/// </summary>
internal sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default)
    {
        return await _context.Set<User>()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, ct);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<User>().FindAsync([id], ct);
    }

    public void Add(User user)
    {
        _context.Set<User>().Add(user);
    }
}
