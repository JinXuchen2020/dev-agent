namespace AgentPlatform.Api.Models;

/// <summary>
/// 平台模型目录项（GET /api/v1/models）。仅暴露模型标识，不含任何密钥。
/// <see cref="IsTenantOwned"/> 指示该项是否为当前租户自配（BYO）模型；平台内置为 false。
/// </summary>
public sealed record PlatformModelDto(
    string ModelId,
    string Provider,
    string DisplayName,
    bool IsTenantOwned = false);
