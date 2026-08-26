using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 测试专用 ITenantModelClientResolver：恒返回空列表 → ModelRouter/模型目录回退平台 stub 模型。
///
/// 动机（2026-08-26 CI 修复）：BDD 场景经 TenantCredentialsSteps 写入假 BYO 凭据
/// （sk-bdd-test-key-not-real）后，SendMessage 会经 TenantModelClientResolver 构建真实
/// OpenAI 客户端出站 → 401。隔离必须在**测试组合根**做，而非让生产解析器读
/// ModelClient:Provider 配置——否则 QuickStart（同样 Provider=Stub）下用户自配模型
/// 会被误杀，GET /api/v1/models 不再列出「我的」模型。
/// </summary>
public sealed class StubTenantModelClientResolver : ITenantModelClientResolver
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TenantModelResolution>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantModelResolution>>(Array.Empty<TenantModelResolution>());
}
