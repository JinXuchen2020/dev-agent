#nullable disable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Models;
using AgentPlatform.Infrastructure.Models.RoutingMiddleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Models;

/// <summary>
/// 验证 F13 模型客户端租户隔离（多凭据）：有 BYO 凭据返回租户专属客户端列表，无/全禁用则返回空列表（回退平台）。
/// F28 补充：Stub 模式（集成/演示）下短路返回空列表，绝不解析/解密租户 BYO 凭据，避免触发真实 LLM 网络请求。
/// </summary>
public class TenantModelClientResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static TenantModelClientResolver Create(
        ITenantCredentialResolver resolver,
        IApiKeyEncryptionService encryption,
        IConfiguration configuration = null)
    {
        return new TenantModelClientResolver(
            resolver,
            encryption,
            Substitute.For<ILogger<TenantModelClientResolver>>(),
            Substitute.For<ILogger<ModelTelemetryDecorator>>());
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmpty_WhenNoCredential()
    {
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(new List<TenantCredentialSetting>());
        var sut = Create(resolver, Substitute.For<IApiKeyEncryptionService>());

        var result = await sut.ResolveAsync(TenantId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmpty_WhenAllCredentialsDisabled()
    {
        var creds = new List<TenantCredentialSetting>
        {
            new(Guid.NewGuid(), TenantId, CredentialCategory.Model, "My DeepSeek", "DeepSeek", "enc", "sk-abcd1234",
                "https://api.deepseek.com", "deepseek-chat", isEnabled: false),
        };
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(creds);
        var sut = Create(resolver, Substitute.For<IApiKeyEncryptionService>());

        var result = await sut.ResolveAsync(TenantId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsOneClientPerEnabledCredential_WithNormalizedProviderAndModel()
    {
        var creds = new List<TenantCredentialSetting>
        {
            new(Guid.NewGuid(), TenantId, CredentialCategory.Model, "My DeepSeek", "DeepSeek", "enc1", "sk-abcd1234",
                "https://api.deepseek.com", "deepseek-chat", isEnabled: true),
            new(Guid.NewGuid(), TenantId, CredentialCategory.Model, "My GPT", "OpenAI", "enc2", "sk-efgh5678",
                null, "gpt-4o", isEnabled: true),
            // 禁用项应被排除
            new(Guid.NewGuid(), TenantId, CredentialCategory.Model, "Disabled", "VLLM", "enc3", "sk-ijkl9012",
                "http://vllm", "vllm-model", isEnabled: false),
        };
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(creds);
        var encryption = Substitute.For<IApiKeyEncryptionService>();
        encryption.DecryptKey("enc1").Returns("real-key-1");
        encryption.DecryptKey("enc2").Returns("real-key-2");
        var sut = Create(resolver, encryption);

        var result = await sut.ResolveAsync(TenantId);

        Assert.Equal(2, result.Count);
        var deepseek = result.Single(r => r.Candidates[0].Provider == "deepseek");
        Assert.IsType<ModelTelemetryDecorator>(deepseek.Client);
        Assert.Equal("deepseek-chat", deepseek.Candidates[0].ModelId);
        Assert.Equal(100, deepseek.Candidates[0].Priority);

        var openai = result.Single(r => r.Candidates[0].Provider == "openai");
        Assert.Equal("gpt-4o", openai.Candidates[0].ModelId);
    }

    [Fact]
    public async Task ResolveAsync_AlwaysConsultsCredentials_IgnoringProviderConfig()
    {
        // 契约更新（F13 多 BYO 落地时移除 F28 的 Stub 短路，见 TenantModelClientResolver 文件头注释）：
        // 解析器不再受全局 ModelClient:Provider 影响，始终读取租户真实凭据；
        // 集成环境的确定性由「无启用凭据 → 空列表 → 平台回退」保证。
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(TenantId, CredentialCategory.Model, Arg.Any<CancellationToken>())
            .Returns(new List<TenantCredentialSetting>());
        var encryption = Substitute.For<IApiKeyEncryptionService>();
        var sut = Create(resolver, encryption);

        var result = await sut.ResolveAsync(TenantId);

        Assert.Empty(result);
        await resolver.Received(1).ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<CredentialCategory>(), Arg.Any<CancellationToken>());
        encryption.DidNotReceive().DecryptKey(Arg.Any<string>()); // 无凭据则绝不解密
    }
}
