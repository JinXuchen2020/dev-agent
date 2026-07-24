using System.Security.Cryptography;
using System.Text;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// PBKDF2 password hasher using the built-in <see cref="Rfc2898DeriveBytes"/>.
/// Stored format: <c>$pbkdf2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c>.
/// Salt is randomly generated per hash; iterations are fixed at 100,000 (SHA-256).
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32; // 256-bit
    private const string Prefix = "$pbkdf2$";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;
        if (!hash.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = hash.Split('$');
        // parts: ["", "pbkdf2", "<iterations>", "<salt>", "<hash>"]
        if (parts.Length != 5)
            return false;
        if (!int.TryParse(parts[2], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
