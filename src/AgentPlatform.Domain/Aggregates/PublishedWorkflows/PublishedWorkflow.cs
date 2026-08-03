using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.PublishedWorkflows;

/// <summary>
/// 已发布工作流聚合（F22）。把一个工作流一键暴露为对外的可执行能力：
/// API 模式 = 受 API Key 鉴权的 HTTP 端点；MCP 模式 = 平台内 MCP tool。
/// 实体遵循租户隔离（<see cref="ITenantScoped"/>），由 AppDbContext 的查询过滤器强制；
/// <see cref="Slug"/> 在同一租户内唯一，作为外部调用地址。
/// </summary>
public sealed class PublishedWorkflow : ITenantScoped
{
    /// <summary>获取已发布记录的唯一标识符（由调用方以 ValueGeneratedNever 显式提供）。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取拥有该发布记录的租户标识符（租户隔离键）。</summary>
    public Guid TenantId { get; private init; }

    /// <summary>获取被发布的工作流标识符。</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>获取对外调用的公开地址段（同一租户内唯一，URL 安全）。</summary>
    public string Slug { get; private init; } = null!;

    /// <summary>获取发布形态（Api / Mcp）。</summary>
    public PublishMode Mode { get; private init; }

    /// <summary>
    /// 获取绑定的 API Key 标识符（可选）。为 null 时该租户任意有效 API Key 均可调用；
    /// 非 null 时仅该特定 Key 可调用（ tighter 调用方隔离）。
    /// </summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>
    /// 获取输入契约（JSON Schema 片段，可选）。运行时按其中的 <c>required</c> 字段做轻量校验，
    /// 并作为 API/MCP 消费者的输入说明。
    /// </summary>
    public string? InputSchemaJson { get; private set; }

    /// <summary>获取是否已启用（禁用后 slug 失效、MCP 列表移除）。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>获取发布创建的 UTC 时间。</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>获取发布记录最近更新的 UTC 时间。</summary>
    public DateTime UpdatedAt { get; private set; }

    private PublishedWorkflow() { }

    /// <summary>
    /// 初始化一条已发布工作流记录（默认启用）。
    /// </summary>
    public PublishedWorkflow(
        Guid id,
        Guid tenantId,
        Guid workflowId,
        string slug,
        PublishMode mode,
        Guid? apiKeyId = null,
        string? inputSchemaJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        Id = id;
        TenantId = tenantId;
        WorkflowId = workflowId;
        Slug = slug;
        Mode = mode;
        ApiKeyId = apiKeyId;
        InputSchemaJson = inputSchemaJson;
        IsEnabled = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>重新绑定 API Key（null = 放开为租户任意有效 Key）。</summary>
    public void SetApiKey(Guid? apiKeyId)
    {
        ApiKeyId = apiKeyId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更新输入契约（JSON Schema 片段）。</summary>
    public void SetInputSchema(string? inputSchemaJson)
    {
        InputSchemaJson = inputSchemaJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>启用该发布记录。</summary>
    public void Enable()
    {
        IsEnabled = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>禁用该发布记录（slug 失效、MCP 列表移除）。</summary>
    public void Disable()
    {
        IsEnabled = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
