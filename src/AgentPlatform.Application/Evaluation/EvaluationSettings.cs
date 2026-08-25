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

    /// <summary>
    /// F34 评估门禁默认通过率阈值：Score &lt; GateMinPassRate 时门禁判定不通过（HTTP 422）。
    /// 请求显式指定 minPassRate 时覆盖此值。默认 0.8。
    /// </summary>
    public double GateMinPassRate { get; set; } = 0.8;
}
