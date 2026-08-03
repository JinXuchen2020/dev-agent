using AgentPlatform.Application.PublishedWorkflows;

namespace AgentPlatform.Application.PublishedWorkflows.Queries.ListMcpTools;

/// <summary>
/// 列出当前租户下所有已启用、MCP 形态的已发布工作流（F22，供 MCP tools/list 使用）。
/// </summary>
public record ListMcpToolsQuery(
    Guid TenantId
) : MediatR.IRequest<IReadOnlyList<McpToolInfo>>;
