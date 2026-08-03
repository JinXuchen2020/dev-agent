using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.PublishedWorkflows.Queries.ListMcpTools;

internal sealed class ListMcpToolsQueryHandler
    : IRequestHandler<ListMcpToolsQuery, IReadOnlyList<McpToolInfo>>
{
    private readonly IPublishedWorkflowRepository _publishedRepo;

    public ListMcpToolsQueryHandler(IPublishedWorkflowRepository publishedRepo)
    {
        _publishedRepo = publishedRepo;
    }

    public async Task<IReadOnlyList<McpToolInfo>> Handle(ListMcpToolsQuery request, CancellationToken ct)
    {
        var published = await _publishedRepo.GetByTenantAndModeAsync(
            request.TenantId, PublishMode.Mcp, enabledOnly: true, ct);

        // 避免按工作流逐一查名的 N+1：MCP tool 的 name 已是 slug，description 同样用 slug
        // （轻量 v1 形态，无需回查工作流名；若需展示名可后续在 PublishedWorkflow 上冗余存储）。
        var result = new List<McpToolInfo>(published.Count);
        foreach (var p in published)
        {
            result.Add(new McpToolInfo(
                Name: p.Slug,
                Description: p.Slug,
                InputSchema: p.InputSchemaJson));
        }

        return result;
    }
}
