using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Api.Models;

/// <summary>
/// 新建租户凭据设置的请求体。
/// <see cref="ApiKey"/> 为明文入站（首次配置必填），仅服务端使用，加密后即刻丢弃，绝不落库/回显。
/// <see cref="Name"/> 为该凭据在租户列表中的显示名（如 "My GPT-4o"）。
/// </summary>
public sealed record CreateTenantCredentialRequest(
    CredentialCategory Category,
    string Name,
    string Provider,
    string ApiKey,
    string? BaseUrl = null,
    string? ModelName = null,
    bool IsEnabled = true);
