namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Abstraction for encrypting and decrypting API key secrets at rest.
/// Implementations use <see cref="IAesEncryptor"/> under the hood but add
/// key-prefix extraction and format validation specific to API key management.
/// </summary>
public interface IApiKeyEncryptionService
{
    /// <summary>
    /// Encrypts a plaintext API key and returns a tuple of (encryptedKey, keyPrefix).
    /// </summary>
    /// <param name="plaintextKey">The plaintext API key to encrypt.</param>
    /// <returns>A tuple containing the encrypted ciphertext and the first 8 chars prefix.</returns>
    (string EncryptedKey, string KeyPrefix) EncryptKey(string plaintextKey);

    /// <summary>
    /// Decrypts an encrypted API key back to plaintext.
    /// </summary>
    /// <param name="encryptedKey">The encrypted ciphertext.</param>
    /// <returns>The plaintext API key.</returns>
    string DecryptKey(string encryptedKey);
}
