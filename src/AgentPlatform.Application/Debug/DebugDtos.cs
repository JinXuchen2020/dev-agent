using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Debug;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;

namespace AgentPlatform.Application.Debug;

/// <summary>
/// Response / request contracts and mapping helpers for workflow debugging (F25).
/// </summary>
public static class DebugDtos
{
    // ── Responses ──

    /// <summary>Result of starting (or resetting) a debug session.</summary>
    /// <param name="SessionId">The newly created debug session identifier.</param>
    /// <param name="WorkflowId">The workflow being debugged.</param>
    /// <param name="Status">The initial session status (always <see cref="DebugSessionStatus.Initialized"/>).</param>
    public sealed record StartDebugSessionResponse(
        Guid SessionId,
        Guid WorkflowId,
        DebugSessionStatus Status);

    /// <summary>Result of executing a single debug step.</summary>
    /// <param name="Executed">Whether a Pending node was actually executed in this step.</param>
    /// <param name="WorkflowState">The resulting workflow execution state.</param>
    /// <param name="Node">The node that was executed, if any.</param>
    /// <param name="Variables">The accumulated blackboard variables after this step.</param>
    public sealed record DebugStepResponse(
        bool Executed,
        WorkflowState WorkflowState,
        StepSnapshot? Node,
        IReadOnlyDictionary<string, string> Variables);

    /// <summary>Result of resuming a debugged workflow to completion.</summary>
    /// <param name="WorkflowState">The terminal workflow execution state.</param>
    /// <param name="Variables">The accumulated blackboard variables at completion.</param>
    public sealed record DebugResumeResponse(
        WorkflowState WorkflowState,
        IReadOnlyDictionary<string, string> Variables);

    /// <summary>Result of re-running a single node within a debug session.</summary>
    /// <param name="Executed">Whether the targeted node was actually (re-)executed.</param>
    /// <param name="WorkflowState">The resulting workflow execution state.</param>
    /// <param name="Node">The node that was re-run, if any.</param>
    /// <param name="Variables">The accumulated blackboard variables after the retry.</param>
    public sealed record DebugRetryResponse(
        bool Executed,
        WorkflowState WorkflowState,
        StepSnapshot? Node,
        IReadOnlyDictionary<string, string> Variables);

    /// <summary>Payload carrying the accumulated blackboard variables for variable monitoring.</summary>
    /// <param name="Variables">The accumulated blackboard variables captured so far.</param>
    public sealed record DebugVariablesResponse(
        IReadOnlyDictionary<string, string> Variables);

    // ── Mapping helpers ──

    /// <summary>Maps a workflow execution state to the closest debug session status.</summary>
    /// <param name="state">The workflow execution state.</param>
    /// <returns>The corresponding <see cref="DebugSessionStatus"/>.</returns>
    public static DebugSessionStatus Map(WorkflowState state) => state switch
    {
        WorkflowState.Completed => DebugSessionStatus.Completed,
        WorkflowState.Paused => DebugSessionStatus.Paused,
        WorkflowState.RolledBack => DebugSessionStatus.RolledBack,
        WorkflowState.Failed => DebugSessionStatus.Failed,
        _ => DebugSessionStatus.Running,
    };

    /// <summary>Builds a <see cref="Blackboard"/> from a variable dictionary.</summary>
    /// <param name="variables">The variable dictionary to load.</param>
    /// <returns>A blackboard pre-populated with the supplied entries.</returns>
    public static Blackboard ToBlackboard(IReadOnlyDictionary<string, string> variables)
    {
        var blackboard = Blackboard.Empty;
        foreach (var kv in variables)
            blackboard.Set(kv.Key, kv.Value);
        return blackboard;
    }

    /// <summary>
    /// Creates a fresh debug session and resets the workflow's node/step states to Pending,
    /// so a new debugging pass starts from a clean slate. Shared by start and reset.
    /// </summary>
    /// <param name="workflowId">The workflow to debug.</param>
    /// <param name="tenantId">The current tenant identifier.</param>
    /// <param name="initialContext">Optional debugging initial context (null = keep existing).</param>
    /// <param name="workflowRepo">Workflow repository.</param>
    /// <param name="sessionRepo">Debug session repository.</param>
    /// <param name="auditLogRepo">Audit log repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created debug session response.</returns>
    public static async Task<StartDebugSessionResponse> StartOrResetAsync(
        Guid workflowId,
        Guid tenantId,
        string? initialContext,
        IWorkflowRepository workflowRepo,
        IDebugSessionRepository sessionRepo,
        IAuditLogRepository auditLogRepo,
        CancellationToken ct)
    {
        var wf = await workflowRepo.GetByIdAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' was not found.");

        wf.Reset();
        if (!string.IsNullOrWhiteSpace(initialContext))
            wf.UpdateContext(initialContext);
        workflowRepo.Update(wf);

        var session = new DebugSession(Guid.NewGuid(), workflowId, tenantId);
        sessionRepo.Add(session);

        auditLogRepo.Add(AuditLog.Record(
            tenantId, AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.DebugRun, "Workflow",
            entityId: workflowId, details: $"Started debug session {session.Id}"));

        return new StartDebugSessionResponse(session.Id, workflowId, session.Status);
    }
}
