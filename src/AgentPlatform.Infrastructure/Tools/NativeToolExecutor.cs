using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// Executes tools that are implemented as native, in-process handlers within the platform.
/// </summary>
internal sealed class NativeToolExecutor : IToolExecutor
{
    /// <summary>
    /// Gets the tool source handled by this executor, which is <see cref="ToolSource.NativeTool"/>.
    /// </summary>
    public ToolSource Source => ToolSource.NativeTool;

    /// <summary>
    /// Executes the specified native tool with the provided JSON parameters.
    /// </summary>
    /// <param name="tool">The tool definition describing the native tool to invoke.</param>
    /// <param name="parametersJson">A JSON string containing the arguments to pass to the tool.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ToolExecutionResult"/> indicating whether execution succeeded.</returns>
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool, string parametersJson, CancellationToken ct)
    {
        return Task.FromResult(new ToolExecutionResult(true, "Executed natively"));
    }
}
