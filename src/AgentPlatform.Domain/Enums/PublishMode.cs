namespace AgentPlatform.Domain.Enums;

/// <summary>
/// 工作流发布形态（F22）。决定已发布工作流对外暴露的方式。
/// </summary>
public enum PublishMode
{
    /// <summary>发布为受 API Key 鉴权的 HTTP 端点（POST /api/v1/published-workflows/{slug}）。</summary>
    Api = 0,

    /// <summary>发布为平台内 MCP tool（纳入 tools/list + tools/call）。</summary>
    Mcp = 1,
}
