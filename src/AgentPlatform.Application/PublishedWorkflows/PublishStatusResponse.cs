namespace AgentPlatform.Application.PublishedWorkflows;

/// <summary>
/// 发布状态响应（F22）。用于 GET /workflows/{id}/publish 与 POST /workflows/{id}/publish 的返回。
/// </summary>
public sealed record PublishStatusResponse(
    Guid Id,
    Guid WorkflowId,
    string Slug,
    string Mode,
    bool IsEnabled,
    Guid? ApiKeyId,
    string? InputSchemaJson,
    DateTime CreatedAt);
