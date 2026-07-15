using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.Tools;

/// <summary>
/// Dispatches tool execution requests to the appropriate <see cref="IToolExecutor"/> based on the tool's
/// <see cref="ToolSource"/>, performing validation and logging along the way.
/// </summary>
public sealed class ToolCallingDispatcher
{
    private readonly IToolRegistry _toolRegistry;
    private readonly Dictionary<ToolSource, IToolExecutor> _executors;
    private readonly ILogger<ToolCallingDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallingDispatcher"/> class.
    /// </summary>
    /// <param name="toolRegistry">The registry used to look up tool definitions by name.</param>
    /// <param name="executors">The collection of executors, keyed by their <see cref="IToolExecutor.Source"/>.</param>
    /// <param name="logger">The logger used to record dispatch decisions and errors.</param>
    public ToolCallingDispatcher(
        IToolRegistry toolRegistry,
        IEnumerable<IToolExecutor> executors,
        ILogger<ToolCallingDispatcher> logger)
    {
        _toolRegistry = toolRegistry;
        _executors = executors.ToDictionary(e => e.Source);
        _logger = logger;
    }

    /// <summary>
    /// Dispatches execution of the named tool with the provided JSON-encoded parameters to the
    /// executor registered for the tool's source.
    /// </summary>
    /// <param name="toolName">The unique name of the tool to dispatch.</param>
    /// <param name="parametersJson">A JSON string containing the parameters to pass to the tool.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the outcome of the tool execution.</returns>
    /// <exception cref="ArgumentException">Thrown when the tool is not found in the registry.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the tool is disabled or no executor is registered for its source.</exception>
    public async Task<ToolExecutionResult> DispatchAsync(
        string toolName, string parametersJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var tool = await _toolRegistry.GetByNameAsync(toolName, ct);
        if (tool == null)
        {
            _logger.LogError("Tool '{ToolName}' not found in registry", toolName);
            throw new ArgumentException($"Tool '{toolName}' not found");
        }

        if (!tool.IsEnabled)
        {
            _logger.LogWarning("Tool '{ToolName}' is disabled", toolName);
            throw new InvalidOperationException($"Tool '{toolName}' is disabled");
        }

        if (!_executors.TryGetValue(tool.Source, out var executor))
        {
            _logger.LogError("No executor for tool source '{Source}' (tool: {ToolName})", tool.Source, toolName);
            throw new InvalidOperationException($"No executor registered for tool source '{tool.Source}'");
        }

        _logger.LogInformation("Dispatching tool '{ToolName}' via {Source} executor", toolName, tool.Source);
        return await executor.ExecuteAsync(tool, parametersJson, ct);
    }
}
