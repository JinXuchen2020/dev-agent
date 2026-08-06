namespace AgentPlatform.Domain.Enums;

/// <summary>
/// How an evaluation case's actual output is compared against its expected output (F24).
/// </summary>
public enum EvaluationMatchMode
{
    /// <summary>Exact string equality (case-sensitive).</summary>
    Exact = 0,

    /// <summary>Substring containment, ordinal ignore-case.</summary>
    Contains = 1
}
