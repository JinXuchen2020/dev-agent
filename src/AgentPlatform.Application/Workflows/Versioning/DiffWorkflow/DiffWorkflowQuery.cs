using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning.DiffWorkflow;

/// <summary>
/// Computes a structured diff between two workflow definitions for the current tenant:
/// the current graph of <see cref="WorkflowId"/> versus either a saved-version pair
/// (<see cref="FromVersionId"/> / <see cref="ToVersionId"/>) or another workflow's current
/// graph (<see cref="OtherWorkflowId"/>). When neither is supplied the current graph is
/// compared against the latest saved version (or an empty base when no versions exist).
/// Read-only; returns <c>null</c> when the workflow or an explicit version is missing or
/// belongs to a different tenant.
/// </summary>
public sealed record DiffWorkflowQuery(
    Guid WorkflowId,
    Guid? FromVersionId,
    Guid? ToVersionId,
    Guid? OtherWorkflowId,
    Guid TenantId)
    : IRequest<WorkflowDiffDto?>;

/// <summary>Structured diff of two workflow definitions.</summary>
public sealed record WorkflowDiffDto(
    Guid WorkflowId,
    string FromLabel,
    string ToLabel,
    IReadOnlyList<WorkflowVersionNode> AddedNodes,
    IReadOnlyList<WorkflowVersionNode> RemovedNodes,
    IReadOnlyList<ChangedWorkflowNode> ChangedNodes,
    IReadOnlyList<WorkflowDiffEdgeDto> AddedEdges,
    IReadOnlyList<WorkflowDiffEdgeDto> RemovedEdges,
    bool ContextChanged,
    string? ContextBefore,
    string? ContextAfter);

/// <summary>A node present in both definitions whose attributes changed.</summary>
public sealed record ChangedWorkflowNode(
    Guid Id, WorkflowVersionNode Before, WorkflowVersionNode After);

/// <summary>An edge in the diff, identified by its endpoint node names (stable across edits).</summary>
public sealed record WorkflowDiffEdgeDto(string SourceName, string TargetName, string? Label);

internal sealed class DiffWorkflowQueryHandler(
    IWorkflowRepository workflowRepository,
    IWorkflowVersionRepository versionRepository)
    : IRequestHandler<DiffWorkflowQuery, WorkflowDiffDto?>
{
    /// <summary>Resolve the two snapshots to compare and their human labels.</summary>
    public async Task<WorkflowDiffDto?> Handle(DiffWorkflowQuery request, CancellationToken ct)
    {
        var wf = await workflowRepository.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null;

        WorkflowGraphSnapshot baseSnapshot;
        WorkflowGraphSnapshot compareSnapshot;
        string fromLabel;
        string toLabel;

        if (request.OtherWorkflowId.HasValue)
        {
            var other = await workflowRepository.GetByIdAsync(request.OtherWorkflowId.Value, ct);
            if (other is null || other.TenantId != request.TenantId)
                return null;
            baseSnapshot = WorkflowGraphSnapshot.FromWorkflow(other);
            compareSnapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
            fromLabel = other.Name;
            toLabel = wf.Name;
        }
        else if (request.FromVersionId.HasValue && request.ToVersionId.HasValue)
        {
            var from = await versionRepository.GetByIdAsync(request.FromVersionId.Value, ct);
            var to = await versionRepository.GetByIdAsync(request.ToVersionId.Value, ct);
            if (from is null || to is null
                || from.WorkflowId != request.WorkflowId || to.WorkflowId != request.WorkflowId)
                return null;
            baseSnapshot = WorkflowGraphSnapshot.FromJson(from.SnapshotJson);
            compareSnapshot = WorkflowGraphSnapshot.FromJson(to.SnapshotJson);
            fromLabel = $"v{from.VersionNumber}";
            toLabel = $"v{to.VersionNumber}";
        }
        else if (request.FromVersionId.HasValue)
        {
            // Compare a specific saved version against the current live workflow graph
            // (the per-version "对比" action in the UI targets this branch).
            var from = await versionRepository.GetByIdAsync(request.FromVersionId.Value, ct);
            if (from is null || from.WorkflowId != request.WorkflowId)
                return null;
            baseSnapshot = WorkflowGraphSnapshot.FromJson(from.SnapshotJson);
            compareSnapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
            fromLabel = $"v{from.VersionNumber}";
            toLabel = "current";
        }
        else
        {
            // Default: current graph vs latest saved version (empty base when none exist).
            var versions = await versionRepository.ListByWorkflowAsync(request.WorkflowId, 0, 1, ct);
            var latest = versions.Count > 0 ? versions[0] : null;
            baseSnapshot = latest is null
                ? new WorkflowGraphSnapshot(
                    string.Empty,
                    System.Array.Empty<WorkflowVersionNode>(),
                    System.Array.Empty<WorkflowVersionEdge>())
                : WorkflowGraphSnapshot.FromJson(latest.SnapshotJson);
            compareSnapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
            fromLabel = latest is null ? "(no version)" : $"v{latest.VersionNumber}";
            toLabel = "current";
        }

        return Compute(baseSnapshot, compareSnapshot, request.WorkflowId, fromLabel, toLabel);
    }

    /// <summary>Diffs two snapshots node- and edge-wise plus the workflow-level context.</summary>
    /// <remarks>
    /// Nodes are matched by their (unique, human-meaningful) <see cref="WorkflowVersionNode.Name"/>
    /// rather than by <c>Id</c>: <see cref="Workflow.ReplaceGraph"/> regenerates node/edge ids on
    /// every edit, so ids are not stable across versions and would make every comparison report a
    /// full remove+add. Edges are matched by their endpoint names plus label for the same reason.
    /// </remarks>
    private static WorkflowDiffDto Compute(
        WorkflowGraphSnapshot a, WorkflowGraphSnapshot b,
        Guid workflowId, string fromLabel, string toLabel)
    {
        var aNodes = ToNameMap(a.Nodes);
        var bNodes = ToNameMap(b.Nodes);
        var addedNodes = b.Nodes.Where(n => !aNodes.ContainsKey(n.Name)).ToList();
        var removedNodes = a.Nodes.Where(n => !bNodes.ContainsKey(n.Name)).ToList();
        var changedNodes = new List<ChangedWorkflowNode>();
        foreach (var kv in aNodes)
        {
            if (bNodes.TryGetValue(kv.Key, out var bNode) && !NodeEquals(kv.Value, bNode))
                changedNodes.Add(new ChangedWorkflowNode(bNode.Id, kv.Value, bNode));
        }

        var aNameById = a.Nodes.ToDictionary(n => n.Id, n => n.Name);
        var bNameById = b.Nodes.ToDictionary(n => n.Id, n => n.Name);
        var aEdges = EdgeIndex(a.Edges, aNameById);
        var bEdges = EdgeIndex(b.Edges, bNameById);
        var addedEdges = b.Edges
            .Where(e => EdgeKey(bNameById, e) is { } key && !aEdges.ContainsKey(key))
            .Select(e => ToEdgeDto(e, bNameById))
            .ToList();
        var removedEdges = a.Edges
            .Where(e => EdgeKey(aNameById, e) is { } key && !bEdges.ContainsKey(key))
            .Select(e => ToEdgeDto(e, aNameById))
            .ToList();

        var contextChanged = a.Context != b.Context;
        return new WorkflowDiffDto(
            workflowId, fromLabel, toLabel,
            addedNodes, removedNodes, changedNodes,
            addedEdges, removedEdges,
            contextChanged,
            contextChanged ? a.Context : null,
            contextChanged ? b.Context : null);
    }

    /// <summary>
    /// Builds a name→node map tolerant of duplicate names (first occurrence wins).
    /// <see cref="Workflow.ReplaceGraph"/> enforces unique names, but a legacy
    /// <see cref="WorkflowStep"/>-only workflow may carry duplicates; we must not throw on it.
    /// </summary>
    private static Dictionary<string, WorkflowVersionNode> ToNameMap(IReadOnlyList<WorkflowVersionNode> nodes)
    {
        var dict = new Dictionary<string, WorkflowVersionNode>();
        foreach (var n in nodes)
        {
            if (!dict.ContainsKey(n.Name))
                dict[n.Name] = n;
        }

        return dict;
    }

    /// <summary>Stable edge key: "sourceName→targetName\u0001label".</summary>
    private static string? EdgeKey(
        IReadOnlyDictionary<Guid, string> nameById, WorkflowVersionEdge e)
    {
        if (!nameById.TryGetValue(e.Source, out var s) || !nameById.TryGetValue(e.Target, out var t))
            return null;
        return $"{s}\u2192{t}\u0001{e.Label ?? string.Empty}";
    }

    /// <summary>Indexes edges by their stable name-based key, skipping edges with dangling endpoints.</summary>
    private static Dictionary<string, WorkflowVersionEdge> EdgeIndex(
        IReadOnlyList<WorkflowVersionEdge> edges, IReadOnlyDictionary<Guid, string> nameById)
    {
        var dict = new Dictionary<string, WorkflowVersionEdge>();
        foreach (var e in edges)
        {
            var key = EdgeKey(nameById, e);
            if (key is not null)
                dict[key] = e;
        }

        return dict;
    }

    /// <summary>Resolves an edge's endpoint node ids to names for presentation.</summary>
    private static WorkflowDiffEdgeDto ToEdgeDto(
        WorkflowVersionEdge e, IReadOnlyDictionary<Guid, string> nameById) =>
        new(
            nameById.TryGetValue(e.Source, out var s) ? s : e.Source.ToString(),
            nameById.TryGetValue(e.Target, out var t) ? t : e.Target.ToString(),
            e.Label);

    private static bool NodeEquals(WorkflowVersionNode x, WorkflowVersionNode y) =>
        x.Type == y.Type
        && x.Name == y.Name
        && x.X == y.X
        && x.Y == y.Y
        && string.Equals(x.ConfigJson, y.ConfigJson, System.StringComparison.Ordinal)
        && x.AssignedAgentId == y.AssignedAgentId;
}
