using AgentPlatform.Domain.Aggregates.ApiKeys;
using Xunit;

namespace AgentPlatform.Application.Tests.Security;

public sealed class ApiKeyDomainTests
{
    private static ApiKey CreateActiveKey() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "enc-key", "ak_abc12", "Test Key", "Admin");

    [Fact]
    public void Rotate_AdvancesVersion()
    {
        var key = CreateActiveKey();
        Assert.Equal(1, key.KeyVersion);

        key.Rotate("enc-key-v2", "ak_rotat");

        Assert.Equal(2, key.KeyVersion);
        Assert.Equal("enc-key-v2", key.EncryptedKeyHash);
        Assert.Equal("ak_rotat", key.KeyPrefix);
    }

    [Fact]
    public void Rotate_ThrowsOnInactiveKey()
    {
        var key = CreateActiveKey();
        key.Revoke();
        Assert.False(key.IsActive);

        Assert.Throws<InvalidOperationException>(() => key.Rotate("enc-key-v2", "ak_rotat"));
    }

    [Fact]
    public void Rotate_ThrowsOnNullEncryptedKey()
    {
        var key = CreateActiveKey();
        Assert.Throws<ArgumentNullException>(() => key.Rotate(null!, "ak_rotat"));
    }

    [Fact]
    public void Rotate_ThrowsOnNullKeyPrefix()
    {
        var key = CreateActiveKey();
        Assert.Throws<ArgumentNullException>(() => key.Rotate("enc-key-v2", null!));
    }

    [Fact]
    public void Revoke_Deactivates()
    {
        var key = CreateActiveKey();
        key.Revoke();

        Assert.False(key.IsActive);
        Assert.NotNull(key.RevokedAt);
    }

    [Fact]
    public void GetRoles_ParsesCsv()
    {
        var key = new ApiKey(Guid.NewGuid(), Guid.NewGuid(), "enc",
            "ak_abc12", "Test", "Admin,Operator");

        var roles = key.GetRoles();

        Assert.Equal(2, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("Operator", roles);
    }

    [Fact]
    public void GetRoles_Empty_ReturnsEmpty()
    {
        var key = new ApiKey(Guid.NewGuid(), Guid.NewGuid(), "enc",
            "ak_abc12", "Test", "");

        Assert.Empty(key.GetRoles());
    }
}
