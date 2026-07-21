using System.Security.Cryptography;
using System.Text;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides AES-256-GCM encryption and decryption for sensitive data at rest.
/// </summary>
public interface IAesEncryptor
{
    /// <summary>
    /// Encrypts plaintext to a hex-encoded ciphertext string with an authenticated nonce.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a hex-encoded ciphertext string back to plaintext.
    /// </summary>
    string Decrypt(string ciphertext);
}
