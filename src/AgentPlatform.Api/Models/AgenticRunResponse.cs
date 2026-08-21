namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API response payload returned after running an autonomous agentic agent.
/// </summary>
/// <param name="FinalAnswer">The agent's final answer once it stops calling tools.</param>
/// <param name="Iterations">The number of control-loop iterations executed.</param>
/// <param name="TotalTokensIn">Total prompt tokens consumed across the loop.</param>
/// <param name="TotalTokensOut">Total completion tokens consumed across the loop.</param>
/// <param name="Trace">The per-step execution trace (tool calls and their results).</param>
public record AgenticRunResponse(
    string FinalAnswer,
    int Iterations,
    int TotalTokensIn,
    int TotalTokensOut,
    IReadOnlyList<AgenticTraceStepResponse> Trace);

/// <summary>
/// A single step in an agentic run trace.
/// </summary>
/// <param name="Iteration">The control-loop iteration this step belongs to.</param>
/// <param name="ToolName">The tool invoked, if any.</param>
/// <param name="ArgumentsJson">The JSON arguments passed to the tool.</param>
/// <param name="Result">The tool result (or final answer context).</param>
/// <param name="Success">Whether the step succeeded.</param>
public record AgenticTraceStepResponse(
    int Iteration,
    string? ToolName,
    string? ArgumentsJson,
    string? Result,
    bool Success);
