namespace AgentPlatform.Api.Models;

/// <summary>
/// 探测供应商模型清单的请求体。<see cref="ApiKey"/> 仅用于本次一次性出站探测，绝不落库、绝不写日志。
/// </summary>
public sealed record DiscoverModelsRequest(
    string Provider,
    string ApiKey,
    string? BaseUrl = null);
