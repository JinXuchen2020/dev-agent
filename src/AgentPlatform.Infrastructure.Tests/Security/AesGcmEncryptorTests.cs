using System.Security.Cryptography;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Security;

public sealed class AesGcmEncryptorTests : IDisposable
{
    private readonly AesGcmEncryptor _encryptor;
    private readonly ILogger<AesGcmEncryptor> _logger = Substitute.For<ILogger<AesGcmEncryptor>>();

    public AesGcmEncryptorTests()
    {
        // Generate a random 32-byte (64 hex char) key for each test run
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var keyHex = Convert.ToHexStringLower(keyBytes);
        var settings = Options.Create(new SecuritySettings { AesEncryptionKey = keyHex });
        _encryptor = new AesGcmEncryptor(settings, _logger);
    }

    public void Dispose()
    {
        // AesGcmEncryptor has no disposable resources to clean up
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        const string plaintext = "Hello, AES-256-GCM roundtrip test!";

        var ciphertext = _encryptor.Encrypt(plaintext);
        var decrypted = _encryptor.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ThrowsOnNullPlaintext()
    {
        Assert.Throws<ArgumentNullException>(() => _encryptor.Encrypt(null!));
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextPerCall()
    {
        const string plaintext = "Same plaintext, different nonce -> different ciphertext";

        var ciphertext1 = _encryptor.Encrypt(plaintext);
        var ciphertext2 = _encryptor.Encrypt(plaintext);

        Assert.NotEqual(ciphertext1, ciphertext2);
    }

    [Fact]
    public void Decrypt_ThrowsOnTamperedCiphertext()
    {
        const string plaintext = "This message will be tampered with.";
        var ciphertext = _encryptor.Encrypt(plaintext);

        // Flip a hex digit in the middle of the ciphertext to a *different, always-valid* hex char.
        // (A naive (char)(c ^ 1) flip can turn 'a' into '`' — invalid hex — which throws FormatException
        // instead of the expected AuthenticationTagMismatchException, making this test flaky.)
        var chars = ciphertext.ToCharArray();
        var mid = chars.Length / 2;
        chars[mid] = chars[mid] switch
        {
            '0' => '1', '1' => '0',
            '2' => '3', '3' => '2',
            '4' => '5', '5' => '4',
            '6' => '7', '7' => '6',
            '8' => '9', '9' => '8',
            'a' => 'b', 'b' => 'a',
            'c' => 'd', 'd' => 'c',
            'e' => 'f', 'f' => 'e',
            _ => chars[mid]
        };
        var tampered = new string(chars);

        Assert.Throws<AuthenticationTagMismatchException>(() => _encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_ThrowsOnNullCiphertext()
    {
        Assert.Throws<ArgumentNullException>(() => _encryptor.Decrypt(null!));
    }

    [Fact]
    public void Decrypt_ThrowsOnInvalidHexString()
    {
        Assert.Throws<FormatException>(() => _encryptor.Decrypt("not-a-valid-hex-string!"));
    }
}
