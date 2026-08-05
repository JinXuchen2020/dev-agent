using System.Text.Json;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.WorkflowTemplates;

/// <summary>
/// 平台级工作流模板（F23）。用于「模板市场 / 示例库」：随 <c>DatabaseInitializer</c> 种子落地，
/// 对所有租户共享、只读；用户一键克隆为属于自己的 <c>Workflow</c>（见 CloneWorkflowTemplateCommand）。
/// <para>
/// 刻意 <b>不</b> 实现 <see cref="ITenantScoped"/>——模板是平台级共享资源，不受 AppDbContext 的租户
/// 查询过滤器约束（决策 S2）。克隆出的新工作流才带当前租户。
/// </para>
/// </summary>
public sealed class WorkflowTemplate : IAggregateRoot
{
    /// <summary>Gets the unique identifier of the template.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the display name of the template.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets the template category (hardcoded enum, 决策 S4).</summary>
    public WorkflowTemplateCategory Category { get; private set; }

    /// <summary>Gets the optional description shown in the template market.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the serialized workflow graph snapshot (context + nodes + edges), reused by clone.</summary>
    public string SnapshotJson { get; private set; } = null!;

    /// <summary>Gets the raw JSON array of tags (persisted column), used for keyword filtering.</summary>
    public string? TagsJson { get; private set; }

    /// <summary>Gets the tags used for filtering / display in the template market.</summary>
    public IReadOnlyList<string> Tags
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TagsJson))
                return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(TagsJson)
                    ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>Gets the UTC timestamp when the template was created (seed time).</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the (empty) collection of domain events. Templates are immutable seeds and raise no events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <summary>Clears pending domain events. No-op — templates raise no events.</summary>
    public void ClearDomainEvents() { }

    private WorkflowTemplate() { }

    public WorkflowTemplate(
        Guid id,
        string name,
        WorkflowTemplateCategory category,
        string? description,
        string snapshotJson,
        IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);

        Id = id;
        Name = name;
        Category = category;
        Description = description;
        SnapshotJson = snapshotJson;
        TagsJson = tags is null ? null : JsonSerializer.Serialize(tags.ToList());
        CreatedAt = DateTime.UtcNow;
    }
}
