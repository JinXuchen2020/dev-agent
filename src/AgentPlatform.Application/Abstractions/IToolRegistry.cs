using AgentPlatform.Domain.Aggregates.ToolDefinitions;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for registering, retrieving, and unregistering tool definitions.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Retrieves a tool definition by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tool.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result is the tool definition, or <c>null</c> if not found.</returns>
    Task<ToolDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a tool definition by its name.
    /// </summary>
    /// <param name="name">The unique name of the tool.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result is the tool definition, or <c>null</c> if not found.</returns>
    Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered tool definitions.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result is a read-only list of all tool definitions.</returns>
    Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers a tool definition in the registry.
    /// </summary>
    /// <param name="tool">The tool definition to register.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    void Register(ToolDefinition tool, CancellationToken ct = default);

    /// <summary>
    /// Removes a tool definition from the registry by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tool to unregister.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    void Unregister(Guid id, CancellationToken ct = default);
}
