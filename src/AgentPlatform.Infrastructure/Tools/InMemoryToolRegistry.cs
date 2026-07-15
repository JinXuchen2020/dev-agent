using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// In-memory implementation of <see cref="IToolRegistry"/> that stores tool definitions in a concurrent dictionary.
/// </summary>
internal sealed class InMemoryToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<Guid, ToolDefinition> _tools = new();

    /// <summary>
    /// Retrieves a tool definition by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tool.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="ToolDefinition"/>, or <c>null</c> if not found.</returns>
    public Task<ToolDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _tools.TryGetValue(id, out var tool);
        return Task.FromResult(tool);
    }

    /// <summary>
    /// Retrieves a tool definition by its name using a case-insensitive match.
    /// </summary>
    /// <param name="name">The name of the tool to locate.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="ToolDefinition"/>, or <c>null</c> if not found.</returns>
    public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var tool = _tools.Values.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(tool);
    }

    /// <summary>
    /// Retrieves all registered tool definitions.
    /// </summary>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of all registered tool definitions.</returns>
    public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ToolDefinition>>(_tools.Values.ToList());
    }

    /// <summary>
    /// Registers a tool definition so it can be discovered and executed by the platform.
    /// </summary>
    /// <param name="tool">The tool definition to register.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    public void Register(ToolDefinition tool, CancellationToken ct = default)
    {
        if (!_tools.TryAdd(tool.Id, tool))
        {
            throw new InvalidOperationException(
                $"Tool with ID '{tool.Id}' is already registered.");
        }
    }

    /// <summary>
    /// Removes a previously registered tool definition by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tool to unregister.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    public void Unregister(Guid id, CancellationToken ct = default)
    {
        _tools.TryRemove(id, out _);
    }
}
