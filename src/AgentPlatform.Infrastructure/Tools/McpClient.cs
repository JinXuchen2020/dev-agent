using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// Executes tools registered through Model Context Protocol (MCP) servers.
/// </summary>
internal sealed class McpClient : IToolExecutor
{
    /// <summary>
    /// Gets the tool source handled by this executor, which is <see cref="ToolSource.McpServer"/>.
    /// </summary>
    public ToolSource Source => ToolSource.McpServer;

    /// <summary>
    /// Executes the specified MCP tool with the provided JSON parameters.
    /// </summary>
    /// <param name="tool">The tool definition describing the MCP tool to invoke.</param>
    /// <param name="parametersJson">A JSON string containing the arguments to pass to the tool.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ToolExecutionResult"/> indicating whether execution succeeded.</returns>
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool, string parametersJson, CancellationToken ct)
    {
        // TODO(Phase6): 真实化需接入外部 MCP server 并调用工具；当前仍返回伪造成功（A1 仅要求 NativeToolExecutor 真实执行）。
        return Task.FromResult(new ToolExecutionResult(true, "Executed via MCP"));
    }
}
