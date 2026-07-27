#nullable disable
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Models;

/// <summary>
/// 验证 F13 模型客户端租户隔离：有 BYO 凭据返回租户专属客户端，无/禁用则返回 null（回退平台）。
/// </summary>
public class TenantModelClientResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static TenantModelClientResolver Create(
        ITenantCredentialResolver resolver,
        IApiKeyEncryptionService encryption)
    {
        return new TenantModelClientResolver(
            resolver,
            encryption,
            Substitute.For<ILogger<TenantModelClientResolver>>(),
            Substitute.For<ILogger<ModelTelemetryDecorator>>());
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNoCredential()
    {
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantCredentialSetting>(null));
        var sut = Create(resolver, Substitute.For<IApiKeyEncryptionService>());

        var result = await sut.ResolveAsync(TenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenCredentialDisabled()
    {
        var cred = new TenantCredentialSetting(
            Guid.NewGuid(), TenantId, CredentialCategory.Model, "DeepSeek", "enc", "sk-abcd1234",
            "https://api.deepseek.com", "deepseek-chat", isEnabled: false);
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(cred);
        var sut = Create(resolver, Substitute.For<IApiKeyEncryptionService>());

        var result = await sut.ResolveAsync(TenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsTenantClient_WithNormalizedProviderAndModel()
    {
        var cred = new TenantCredentialSetting(
            Guid.NewGuid(), TenantId, CredentialCategory.Model, "DeepSeek", "enc", "sk-abcd1234",
            "https://api.deepseek.com", "deepseek-chat", isEnabled: true);
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(cred);
        var encryption = Substitute.For<IApiKeyEncryptionService>();
        encryption.DecryptKey("enc").Returns("real-key");
        var sut = Create(resolver, encryption);

        var result = await sut.ResolveAsync(TenantId);

        Assert.NotNull(result);
        Assert.IsType<ModelTelemetryDecorator>(result.Client);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("deepseek", candidate.Provider);   // 归一化小写
        Assert.Equal("deepseek-chat", candidate.ModelId);
        Assert.Equal(100, candidate.Priority);
    }
}
