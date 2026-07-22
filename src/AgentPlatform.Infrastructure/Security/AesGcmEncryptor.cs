using System.Security.Cryptography;
using System.Text;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// AES-256-GCM encryptor implementation. Plaintext is never stored in the database.
/// </summary>
internal sealed class AesGcmEncryptor : IAesEncryptor
{
    private readonly byte[] _key;
    private readonly ILogger<AesGcmEncryptor> _logger;

    public AesGcmEncryptor(IOptions<SecuritySettings> settings, ILogger<AesGcmEncryptor> logger)
    {
        _logger = logger;
        var keyHex = settings.Value.AesEncryptionKey;
        if (string.IsNullOrEmpty(keyHex))
            throw new InvalidOperationException("Security:AesEncryptionKey must be configured (64 hex chars).");

        var keyBytes = Convert.FromHexString(keyHex);
        if (keyBytes.Length != 32)
            throw new InvalidOperationException("AesEncryptionKey must be exactly 32 bytes (64 hex characters).");

        _key = keyBytes;
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM. Returns hex-encoded string containing nonce + ciphertext + tag.
    /// Format: nonce (12 bytes) + ciphertext + tag (16 bytes), all hex-encoded.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for GCM

        using var aesGcm = new AesGcm(_key, 16); // 128-bit tag
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);

        return Convert.ToHexStringLower(combined);
    }

    /// <summary>
    /// Decrypts hex-encoded ciphertext produced by <see cref="Encrypt"/>.
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        var combined = Convert.FromHexString(ciphertext);
        if (combined.Length < 12 + 16) // minimum: nonce + empty ciphertext + tag
            throw new CryptographicException("Invalid ciphertext length.");

        var nonce = new byte[12];
        var ciphertextBytes = new byte[combined.Length - 28]; // 12 nonce + 16 tag
        var tag = new byte[16];

        Buffer.BlockCopy(combined, 0, nonce, 0, 12);
        Buffer.BlockCopy(combined, 12, ciphertextBytes, 0, ciphertextBytes.Length);
        Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

        var plaintextBytes = new byte[ciphertextBytes.Length];
        using var aesGcm = new AesGcm(_key, 16);

        try
        {
            aesGcm.Decrypt(nonce, ciphertextBytes, tag, plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "AES-GCM decryption failed — possible tampering or wrong key.");
            throw;
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
