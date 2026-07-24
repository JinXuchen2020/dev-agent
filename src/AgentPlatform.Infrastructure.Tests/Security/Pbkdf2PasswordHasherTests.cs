using AgentPlatform.Infrastructure.Security;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Security;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesNonPlaintext_And_Verifies()
    {
        var hash = _hasher.Hash("Admin@123456");
        Assert.NotEqual("Admin@123456", hash);
        Assert.StartsWith("$pbkdf2$", hash);
        Assert.True(_hasher.Verify("Admin@123456", hash));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = _hasher.Hash("Admin@123456");
        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_RejectsTamperedHash()
    {
        var hash = _hasher.Hash("Admin@123456");
        // Flip the last char of the stored hash to simulate tampering.
        var tampered = hash.Length > 0 ? hash[..^1] + (hash[^1] == 'A' ? 'B' : 'A') : hash;
        Assert.False(_hasher.Verify("Admin@123456", tampered));
    }

    [Fact]
    public void Verify_HandlesMalformedHash_Gracefully()
    {
        Assert.False(_hasher.Verify("anything", "not-a-valid-hash"));
        Assert.False(_hasher.Verify("", "not-a-valid-hash"));
    }

    [Fact]
    public void Hash_IsSalted_So_IdenticalPasswords_Differ()
    {
        var a = _hasher.Hash("same-password");
        var b = _hasher.Hash("same-password");
        Assert.NotEqual(a, b);
        Assert.True(_hasher.Verify("same-password", a));
        Assert.True(_hasher.Verify("same-password", b));
    }
}
