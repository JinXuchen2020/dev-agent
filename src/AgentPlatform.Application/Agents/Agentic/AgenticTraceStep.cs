namespace AgentPlatform.Application.Agents.Agentic;

/// <summary>
/// A single step in an agentic run trace — either a tool invocation or the final model answer.
/// </summary>
/// <param name="Iteration">The 1-based iteration in which this step occurred.</param>
/// <param name="ToolName">The tool invoked, or null for a pure model answer step.</param>
/// <param name="ArgumentsJson">The JSON arguments passed to the tool, if any.</param>
/// <param name="Result">The tool output or final answer text.</param>
/// <param name="Success">Whether the step succeeded.</param>
/// <param name="TokensIn">Prompt tokens for this step (0 for tool steps).</param>
/// <param name="TokensOut">Completion tokens for this step (0 for tool steps).</param>
/// <param name="Error">Error detail if the step failed; otherwise null.</param>
public sealed record AgenticTraceStep(
    int Iteration,
    string? ToolName,
    string? ArgumentsJson,
    string? Result,
    bool Success,
    int TokensIn,
    int TokensOut,
    string? Error);
