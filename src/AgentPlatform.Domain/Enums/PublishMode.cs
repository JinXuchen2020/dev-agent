using System.Text.Json.Serialization;

namespace AgentPlatform.Domain.Enums;

/// <summary>
/// 工作流发布形态（F22）。决定已发布工作流对外暴露的方式。
/// 标注 <see cref="JsonStringEnumConverter"/>：前端契约以字符串（"Api"/"Mcp"）收发该枚举，
/// 与全局整数枚举序列化（Program.cs 未注册 JsonStringEnumConverter）解耦，仅影响本枚举。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PublishMode
{
    /// <summary>发布为受 API Key 鉴权的 HTTP 端点（POST /api/v1/published-workflows/{slug}）。</summary>
    Api = 0,

    /// <summary>发布为平台内 MCP tool（纳入 tools/list + tools/call）。</summary>
    Mcp = 1,
}
