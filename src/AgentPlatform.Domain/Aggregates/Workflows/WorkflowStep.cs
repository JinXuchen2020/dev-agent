using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// Represents a single step within a workflow, tracking its execution order,
/// assigned agent, state, result, and any error details. Legacy linear projection
/// of the DAG; also implements <see cref="IWorkflowExecutable"/> for unified execution.
/// </summary>
public sealed class WorkflowStep : IWorkflowExecutable
{
    /// <summary>Gets the unique identifier of the workflow step.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the zero-based execution order of the step within the workflow.</summary>
    public int Order { get; private init; }

    /// <summary>Gets the human-readable name of the step.</summary>
    public string StepName { get; private init; } = null!;

    /// <summary>Gets or sets the unique identifier of the agent assigned to this step, if any.</summary>
    public Guid? AssignedAgentId { get; private set; }

    /// <summary>Gets or sets the current execution state of the step.</summary>
    public WorkflowState State { get; private set; }

    /// <summary>Gets or sets the result produced by the step upon completion, if any.</summary>
    public string? Result { get; private set; }

    /// <summary>Gets or sets the error details recorded when the step fails, if any.</summary>
    public string? ErrorDetail { get; private set; }

    /// <summary>Gets or sets the UTC timestamp when the step was last updated.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStep"/> class.
    /// </summary>
    public WorkflowStep(Guid id, int order, string stepName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);

        Id = id;
        Order = order;
        StepName = stepName;
        State = WorkflowState.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Assigns an agent to this step by its identifier.</summary>
    public void AssignAgent(Guid agentId)
    {
        AssignedAgentId = agentId;
    }

    /// <summary>Sets the execution state of the step and updates the timestamp.</summary>
    public void SetState(WorkflowState state)
    {
        State = state;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Sets the result of the step and marks it as completed.</summary>
    public void SetResult(string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        Result = result;
        State = WorkflowState.Completed;
    }

    /// <summary>Records an error for the step and marks it as failed.</summary>
    public void SetError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ErrorDetail = error;
        State = WorkflowState.Failed;
    }

    /// <inheritdoc/>
    string IWorkflowExecutable.Name => StepName;

    /// <inheritdoc/>
    StepType? IWorkflowExecutable.Type => null;

    /// <inheritdoc/>
    string IWorkflowExecutable.ConfigJson => "{}";
}
