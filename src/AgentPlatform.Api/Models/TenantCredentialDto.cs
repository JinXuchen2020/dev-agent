using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Api.Models;

/// <summary>
/// 返回给前端的租户凭据设置视图模型。绝不包含明文密钥；仅暴露掩码。
/// <see cref="ApiKeyMask"/> 为 •••• + 密钥前 8 字符前缀，明文密钥永不外泄。
/// </summary>
public sealed record TenantCredentialDto(
    CredentialCategory Category,
    string Provider,
    string ApiKeyMask,
    string? BaseUrl,
    string? ModelName,
    bool IsEnabled);
