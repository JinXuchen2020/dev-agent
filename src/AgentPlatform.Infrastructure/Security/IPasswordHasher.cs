namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// Hashes and verifies passwords without storing plaintext.
/// Uses PBKDF2 (built-in <c>Rfc2898DeriveBytes</c>) — no external package required.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password, returning a self-describing stored format.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a previously hashed value.</summary>
    bool Verify(string password, string hash);
}
