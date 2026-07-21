using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// Encrypts and decrypts API key secrets using <see cref="IAesEncryptor"/>.
/// Extracts a human-readable prefix from the plaintext key for identification.
/// </summary>
internal sealed class ApiKeyEncryptionService : IApiKeyEncryptionService
{
    private readonly IAesEncryptor _aesEncryptor;

    public ApiKeyEncryptionService(IAesEncryptor aesEncryptor)
    {
        _aesEncryptor = aesEncryptor;
    }

    /// <summary>
    /// Encrypts a plaintext API key. Returns the ciphertext and first 8 chars as prefix.
    /// </summary>
    public (string EncryptedKey, string KeyPrefix) EncryptKey(string plaintextKey)
    {
        ArgumentNullException.ThrowIfNull(plaintextKey);

        if (plaintextKey.Length < 8)
            throw new ArgumentException("API key must be at least 8 characters long.", nameof(plaintextKey));

        var encrypted = _aesEncryptor.Encrypt(plaintextKey);
        var prefix = plaintextKey[..8];

        return (encrypted, prefix);
    }

    /// <summary>
    /// Decrypts an encrypted API key back to plaintext.
    /// </summary>
    public string DecryptKey(string encryptedKey)
    {
        ArgumentNullException.ThrowIfNull(encryptedKey);
        return _aesEncryptor.Decrypt(encryptedKey);
    }
}
