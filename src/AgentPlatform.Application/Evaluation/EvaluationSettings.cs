namespace AgentPlatform.Application.Evaluation;

/// <summary>
/// Configuration for dataset evaluation runs (F24). Bound from the "Evaluation" config section;
/// sensible defaults apply when the section is absent.
/// </summary>
public sealed class EvaluationSettings
{
    /// <summary>
    /// Hard cap on the number of cases replayed in a single <c>RunEvaluation</c> invocation.
    /// Bounds total runtime / cost; additional cases are ignored. Default 10.
    /// </summary>
    public int MaxCases { get; set; } = 10;
}
