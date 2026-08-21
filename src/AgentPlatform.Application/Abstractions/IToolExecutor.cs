using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides an abstraction for executing a tool definition and returning its execution result.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Gets the source type that this executor is responsible for (e.g., Local, Remote).
    /// </summary>
    ToolSource Source { get; }

    /// <summary>
    /// Executes the specified tool with the provided JSON-encoded parameters.
    /// </summary>
    /// <param name="tool">The tool definition to execute.</param>
    /// <param name="parametersJson">A JSON string containing the parameters to pass to the tool.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the outcome of the tool execution.</returns>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string parametersJson,
        CancellationToken ct = default);
}

/// <summary>
/// Represents the result of executing a tool.
/// </summary>
/// <param name="Success">A value indicating whether the tool executed successfully.</param>
/// <param name="Output">The textual output produced by the tool.</param>
/// <param name="TokenUsage">The token usage incurred during execution, if applicable.</param>
/// <param name="ErrorMessage">A descriptive error message if execution failed, otherwise <c>null</c>.</param>
public record ToolExecutionResult(
    bool Success,
    string Output,
    TokenUsage? TokenUsage = null,
    string? ErrorMessage = null)
{
    /// <summary>Creates a successful result carrying the tool's textual output.</summary>
    public static ToolExecutionResult Ok(string output) => new(true, output);

    /// <summary>Creates a failed result carrying the error message.</summary>
    public static ToolExecutionResult Fail(string error) => new(false, string.Empty, ErrorMessage: error);
}
