namespace AgentPlatform.Application.PublishedWorkflows;

/// <summary>
/// MCP tools/list 返回的单个工具描述（F22，v1 轻量形态）。
/// name = 发布 slug，description = 工作流名，inputSchema = 用户定义的 JSON Schema 片段。
/// </summary>
public sealed record McpToolInfo(
    string Name,
    string Description,
    string? InputSchema);
