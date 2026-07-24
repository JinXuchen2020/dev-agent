namespace AgentPlatform.Domain;

/// <summary>
/// Raised when a workflow cannot be mutated because it is in a state that forbids edits
/// (e.g. <see cref="Enums.WorkflowState.Running"/> or <see cref="Enums.WorkflowState.Paused"/>),
/// or when a run is requested while already running. Surfaced as HTTP 409 by
/// <c>WorkflowConflictExceptionHandler</c>.
/// </summary>
public sealed class WorkflowConflictException : Exception
{
    public WorkflowConflictException(string message) : base(message)
    {
    }

    public WorkflowConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
