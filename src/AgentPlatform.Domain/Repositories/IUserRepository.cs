using AgentPlatform.Domain.Aggregates.Users;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence operations for user aggregates.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Retrieves a user by tenant + email (login lookup).
    /// </summary>
    Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a user by identifier.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new user to the repository.
    /// </summary>
    void Add(User user);
}
