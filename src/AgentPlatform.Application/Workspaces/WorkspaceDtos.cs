namespace AgentPlatform.Application.Workspaces;

/// <summary>
/// 工作空间摘要 DTO（F35）。camelCase 由 API 序列化层保证。
/// </summary>
/// <param name="Id">唯一标识符。</param>
/// <param name="Name">名称（租户内唯一）。</param>
/// <param name="Description">描述（可选）。</param>
/// <param name="IsDefault">是否默认工作空间（不可删除）。</param>
/// <param name="CreatedAt">创建时间（UTC）。</param>
public sealed record WorkspaceDto(Guid Id, string Name, string? Description, bool IsDefault, DateTime CreatedAt);

/// <summary>工作空间成员 DTO（F35）。</summary>
/// <param name="UserId">用户标识符。</param>
/// <param name="Email">用户邮箱。</param>
/// <param name="JoinedAt">分配时间（UTC）。</param>
public sealed record WorkspaceMemberDto(Guid UserId, string Email, DateTime JoinedAt);
