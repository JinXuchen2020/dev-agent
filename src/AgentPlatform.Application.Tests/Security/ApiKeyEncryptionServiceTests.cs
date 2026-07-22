using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Security;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Security;

public sealed class ApiKeyEncryptionServiceTests
{
    private readonly IAesEncryptor _aesEncryptor = Substitute.For<IAesEncryptor>();
    private readonly IApiKeyEncryptionService _service;

    public ApiKeyEncryptionServiceTests()
    {
        _service = new ApiKeyEncryptionService(_aesEncryptor);
    }

    [Fact]
    public void EncryptKey_ReturnsEncryptedKeyAndPrefix()
    {
        const string plaintextKey = "ak_test-key-here-12345";
        const string encryptedHex = "abcdef1234567890abcdef1234567890";
        _aesEncryptor.Encrypt(plaintextKey).Returns(encryptedHex);

        var (encrypted, prefix) = _service.EncryptKey(plaintextKey);

        Assert.Equal(encryptedHex, encrypted);
        Assert.Equal("ak_test-", prefix);
        _aesEncryptor.Received(1).Encrypt(plaintextKey);
    }

    [Fact]
    public void EncryptKey_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => _service.EncryptKey(null!));
    }

    [Fact]
    public void EncryptKey_ThrowsOnShortKey()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.EncryptKey("ab"));
        Assert.Contains("at least 8 characters", ex.Message);
    }

    [Fact]
    public void DecryptKey_ReturnsPlaintext()
    {
        const string encrypted = "deadbeefcafe";
        const string expectedPlaintext = "ak_my-decrypted-key";
        _aesEncryptor.Decrypt(encrypted).Returns(expectedPlaintext);

        var result = _service.DecryptKey(encrypted);

        Assert.Equal(expectedPlaintext, result);
        _aesEncryptor.Received(1).Decrypt(encrypted);
    }

    [Fact]
    public void DecryptKey_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => _service.DecryptKey(null!));
    }

    [Fact]
    public void DecryptKey_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => _service.DecryptKey(""));
    }

    [Fact]
    public void DecryptKey_ThrowsOnWhitespace()
    {
        Assert.Throws<ArgumentException>(() => _service.DecryptKey("   "));
    }
}
