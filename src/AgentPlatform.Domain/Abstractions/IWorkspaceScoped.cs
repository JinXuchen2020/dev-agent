namespace AgentPlatform.Domain.Abstractions;

/// <summary>
/// Defines the contract for entities scoped to a workspace within a tenant (F35).
/// Implemented by business aggregates only — the <c>Workspace</c> container itself and
/// <c>WorkspaceMember</c> are tenant-scoped but NOT workspace-scoped (their WorkspaceId
/// is data, not an isolation scope). Query filtering is enforced globally by
/// <c>AppDbContext</c> (combined tenant + workspace query filter).
/// </summary>
public interface IWorkspaceScoped
{
    /// <summary>
    /// Gets the unique identifier of the workspace that owns this entity.
    /// Assigned automatically on insert by <c>AppDbContext.SaveChangesAsync</c> when left
    /// empty (default workspace of the current tenant context); explicit assignment wins.
    /// </summary>
    Guid WorkspaceId { get; }
}
