using System.Text.Json;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Debug;

/// <summary>
/// A tenant-scoped debug session for a workflow (F25). Captures the cross-node
/// accumulated <see cref="Blackboard"/> variables and the current step pointer so the
/// debug UI can step through a workflow, inspect variables, and recover from errors
/// without affecting production runs.
/// </summary>
public sealed class DebugSession : ITenantScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the session.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the workflow this session debugs.</summary>
    public Guid WorkflowId { get; private set; }

    /// <summary>Gets the tenant that owns this session (auto query-filtered).</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the current lifecycle status of the session.</summary>
    public DebugSessionStatus Status { get; private set; }

    /// <summary>Gets the order of the last executed node (next step hint).</summary>
    public int CurrentStepOrder { get; private set; }

    /// <summary>Gets the serialized blackboard variables (JSON of a string→string dictionary).</summary>
    public string VariablesJson { get; private set; } = "{}";

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the UTC timestamp of the last mutation.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <inheritdoc/>
    public void ClearDomainEvents() { }

    private DebugSession() { }

    /// <summary>Initializes a new debug session for a workflow.</summary>
    public DebugSession(Guid id, Guid workflowId, Guid tenantId)
    {
        Id = id;
        WorkflowId = workflowId;
        TenantId = tenantId;
        Status = DebugSessionStatus.Initialized;
        CurrentStepOrder = 0;
        VariablesJson = "{}";
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Resets the session to a clean slate (fresh debug run).</summary>
    public void Initialize()
    {
        Status = DebugSessionStatus.Initialized;
        CurrentStepOrder = 0;
        VariablesJson = "{}";
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records the outcome of a debug step, persisting the accumulated variables.</summary>
    public void RecordStep(int lastExecutedOrder, DebugSessionStatus status, IReadOnlyDictionary<string, string> variables)
    {
        CurrentStepOrder = lastExecutedOrder;
        Status = status;
        VariablesJson = JsonSerializer.Serialize(variables);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Returns the accumulated blackboard variables.</summary>
    public IReadOnlyDictionary<string, string> GetVariables()
    {
        if (string.IsNullOrWhiteSpace(VariablesJson) || VariablesJson == "{}")
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(VariablesJson)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
