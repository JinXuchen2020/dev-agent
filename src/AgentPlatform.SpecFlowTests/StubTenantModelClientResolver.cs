using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 测试专用 ITenantModelClientResolver：恒返回空列表 → ModelRouter/模型目录回退平台模型。
///
/// 动机：BDD 场景经 TenantCredentialsSteps 写入假 BYO 凭据（sk-bdd-test-key-not-real）后，
/// SendMessage 会经 TenantModelClientResolver 构建真实 OpenAI 客户端出站 → 401。
/// 隔离必须在**测试组合根**做，而非让生产解析器读配置。
/// Integration / Api.Tests 均替换为本实现，确保测试不触发真实 LLM 出站。
/// </summary>
public sealed class StubTenantModelClientResolver : ITenantModelClientResolver
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TenantModelResolution>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantModelResolution>>(Array.Empty<TenantModelResolution>());
}
