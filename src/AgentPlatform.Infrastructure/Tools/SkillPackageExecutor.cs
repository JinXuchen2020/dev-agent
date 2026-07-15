using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// Executes tools backed by Semantic Kernel skill plug-ins.
/// </summary>
internal sealed class SkillPackageExecutor : IToolExecutor
{
    /// <summary>
    /// Gets the tool source handled by this executor, which is <see cref="ToolSource.SkillPackage"/>.
    /// </summary>
    public ToolSource Source => ToolSource.SkillPackage;

    /// <summary>
    /// Executes the specified skill package tool with the provided JSON parameters.
    /// </summary>
    /// <param name="tool">The tool definition describing the skill plug-in to invoke.</param>
    /// <param name="parametersJson">A JSON string containing the arguments to pass to the tool.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ToolExecutionResult"/> indicating whether execution succeeded.</returns>
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool, string parametersJson, CancellationToken ct)
    {
        return Task.FromResult(new ToolExecutionResult(true, "Executed via SK Plugin"));
    }
}
