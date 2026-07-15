namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the types of actions that are recorded in the audit trail.
/// </summary>
public enum AuditActionType
{
    /// <summary>A call was made to a language model.</summary>
    ModelCall,

    /// <summary>Code was executed on behalf of an agent.</summary>
    CodeExecute,

    /// <summary>A configuration value was changed.</summary>
    ConfigChange,

    /// <summary>A user or agent logged into the platform.</summary>
    Login,

    /// <summary>An API key rotation was performed.</summary>
    KeyRotation,

    /// <summary>A workflow execution was started.</summary>
    WorkflowStart,

    /// <summary>A workflow execution was completed.</summary>
    WorkflowComplete,

    /// <summary>A tool was invoked by an agent.</summary>
    ToolCall
}
