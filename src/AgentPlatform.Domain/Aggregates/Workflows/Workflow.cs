using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// Represents a workflow aggregate root, managing an ordered sequence of steps,
/// agent assignments, shared context, and execution state.
/// </summary>
public sealed class Workflow : ITenantScoped, IAggregateRoot
{
    private readonly List<WorkflowStep> _steps = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the unique identifier of the workflow.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets or sets the display name of the workflow.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets a read-only list of steps that comprise the workflow.
    /// </summary>
    public IReadOnlyList<WorkflowStep> Steps => _steps;

    /// <summary>
    /// Gets or sets the current execution state of the workflow.
    /// </summary>
    public WorkflowState CurrentState { get; private set; }

    private readonly Dictionary<string, Guid> _agentAssignments = [];

    /// <summary>
    /// Gets a read-only dictionary mapping step names to their assigned agent IDs.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> AgentAssignments => _agentAssignments;

    /// <summary>
    /// Gets or sets the shared context (JSON) available to all steps in the workflow.
    /// </summary>
    public string Context { get; private set; } = null!;

    /// <summary>
    /// Gets the unique identifier of the tenant that owns this workflow.
    /// </summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the workflow was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the workflow was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate and awaiting dispatch.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Workflow() { }

    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all pending domain events from this aggregate after they have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Initializes a new instance of the <see cref="Workflow"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow.</param>
    /// <param name="name">The display name of the workflow.</param>
    /// <param name="tenantId">The unique identifier of the tenant that owns the workflow.</param>
    public Workflow(Guid id, string name, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        CurrentState = WorkflowState.Pending;
        Context = "{}";
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Appends a step to the end of the workflow.
    /// </summary>
    /// <param name="step">The workflow step to add.</param>
    public void AddStep(WorkflowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the current execution state of the workflow.
    /// </summary>
    /// <param name="state">The new workflow state to assign.</param>
    public void SetState(WorkflowState state)
    {
        CurrentState = state;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the workflow as completed successfully.
    /// </summary>
    public void Complete()
    {
        CurrentState = WorkflowState.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the workflow as rolled back.
    /// </summary>
    public void Rollback()
    {
        CurrentState = WorkflowState.RolledBack;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the shared context available to all steps in the workflow.
    /// </summary>
    /// <param name="context">The new context value (JSON string).</param>
    public void UpdateContext(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        Context = context;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Assigns an agent to a named step within the workflow by storing the agent's identifier.
    /// </summary>
    /// <param name="stepName">The name of the step to assign the agent to.</param>
    /// <param name="agentId">The unique identifier of the agent to assign.</param>
    public void AssignAgent(string stepName, Guid agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        _agentAssignments[stepName] = agentId;
        UpdatedAt = DateTime.UtcNow;
    }
}
