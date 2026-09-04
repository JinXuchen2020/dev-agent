using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workspaces;

/// <summary>
/// 工作空间聚合（F35）。同一租户内的第二层隔离维度：实体按 <c>WorkspaceId</c> 隔离，
/// 切换工作空间后查询仅见当前工作空间数据。
/// 遵循租户隔离（<see cref="ITenantScoped"/>），但<b>不</b>实现 <see cref="IWorkspaceScoped"/>——
/// 它是隔离容器本身；<see cref="IsDefault"/> 的默认工作空间由 DatabaseInitializer 幂等保证存在。
/// </summary>
public sealed class Workspace : ITenantScoped
{
    /// <summary>获取工作空间的唯一标识符（ValueGeneratedNever，显式提供）。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取拥有该工作空间的租户标识符（租户隔离键）。</summary>
    public Guid TenantId { get; private init; }

    /// <summary>获取工作空间名称（同一租户内唯一）。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>获取描述（可选）。</summary>
    public string? Description { get; private set; }

    /// <summary>获取是否为该租户的默认工作空间（恒存在、不可删除）。</summary>
    public bool IsDefault { get; private init; }

    /// <summary>获取创建的 UTC 时间。</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>获取最近更新的 UTC 时间。</summary>
    public DateTime UpdatedAt { get; private set; }

    private Workspace() { }

    /// <summary>
    /// 初始化一个工作空间。
    /// </summary>
    /// <param name="id">唯一标识符。</param>
    /// <param name="tenantId">所属租户。</param>
    /// <param name="name">名称（非空，租户内唯一由仓储/EF 唯一索引保证）。</param>
    /// <param name="description">描述（可选）。</param>
    /// <param name="isDefault">是否默认工作空间。</param>
    public Workspace(Guid id, Guid tenantId, string name, string? description = null, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsDefault = isDefault;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>重命名 / 更新描述（默认工作空间同样允许改名）。</summary>
    public void Update(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
