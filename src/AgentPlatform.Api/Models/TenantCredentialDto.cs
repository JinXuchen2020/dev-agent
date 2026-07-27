using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Api.Models;

/// <summary>
/// 返回给前端的租户凭据设置视图模型。绝不包含明文密钥；仅暴露掩码。
/// <see cref="ApiKeyMask"/> 为 •••• + 密钥前 8 字符前缀，明文密钥永不外泄。
/// 一个租户可拥有多个同类凭据（如多个不同模型），故以列表返回，每项带唯一 <see cref="Id"/> 与显示名 <see cref="Name"/>。
/// </summary>
public sealed record TenantCredentialDto(
    Guid Id,
    string Name,
    CredentialCategory Category,
    string Provider,
    string ApiKeyMask,
    string? BaseUrl,
    string? ModelName,
    bool IsEnabled);
