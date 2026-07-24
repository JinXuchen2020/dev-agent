using System.Security.Claims;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// Mints signed JWT tokens. Single source of truth shared by dev-login and real login endpoints.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Creates a signed JWT containing the supplied claims.
    /// </summary>
    /// <param name="claims">Claims to embed (e.g. sub, name, role, tenant_id).</param>
    /// <param name="lifetime">Token lifetime; defaults to 1 hour when null.</param>
    /// <returns>The serialized JWT string (no "Bearer " prefix).</returns>
    string CreateToken(IEnumerable<Claim> claims, TimeSpan? lifetime = null);
}
