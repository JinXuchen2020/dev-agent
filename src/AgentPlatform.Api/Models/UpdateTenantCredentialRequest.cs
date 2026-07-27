using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Api.Models;

/// <summary>
/// 更新（按 Id）租户凭据设置的请求体。
/// <see cref="ApiKey"/> 为明文入站，仅服务端使用，加密后即刻丢弃，绝不落库/回显。
/// 若 <see cref="ApiKey"/> 为空且该项已有配置，则保留既有密文（仅更新其余字段）。
/// </summary>
public sealed record UpdateTenantCredentialRequest(
    Guid Id,
    string Name,
    CredentialCategory Category,
    string Provider,
    string? ApiKey = null,
    string? BaseUrl = null,
    string? ModelName = null,
    bool IsEnabled = true);
