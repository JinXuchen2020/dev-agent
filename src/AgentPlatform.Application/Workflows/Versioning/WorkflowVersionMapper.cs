using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>Maps <see cref="WorkflowVersion"/> and <see cref="WorkflowGraphSnapshot"/> to view models.</summary>
internal static class WorkflowVersionMapper
{
    /// <summary>Maps a version and its deserialized snapshot to the detail view model.</summary>
    public static WorkflowVersionDetail ToDetail(WorkflowVersion version, WorkflowGraphSnapshot snapshot) => new(
        version.Id,
        version.VersionNumber,
        version.Name,
        version.Note,
        version.CreatedAt,
        version.CreatedBy,
        snapshot.Context,
        snapshot.Nodes.Select(n => new WorkflowVersionNodeView(
            n.Id, n.Type, n.Name, n.X, n.Y, n.ConfigJson, n.AssignedAgentId)).ToList(),
        snapshot.Edges.Select(e => new WorkflowVersionEdgeView(
            e.Id, e.Source, e.Target, e.Label)).ToList());

    /// <summary>Maps a version to the summary view model.</summary>
    public static WorkflowVersionSummary ToSummary(WorkflowVersion version) => new(
        version.Id,
        version.VersionNumber,
        version.Name,
        version.Note,
        version.CreatedAt,
        version.CreatedBy);
}
