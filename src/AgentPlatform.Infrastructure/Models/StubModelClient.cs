using System.Runtime.CompilerServices;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Stub implementation of <see cref="IModelClient"/> that returns canned responses for testing and local development without an LLM backend.
/// </summary>
internal sealed class StubModelClient : IModelClient
{
    private readonly string _stubResponse;
    private readonly string _modelId;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubModelClient"/> class.
    /// </summary>
    /// <param name="stubResponse">The fixed response text returned by every chat call.</param>
    /// <param name="modelId">The identifier reported by this stub client.</param>
    public StubModelClient(string stubResponse = "这是模拟回复，平台已正常运行。", string modelId = "stub-model")
    {
        _stubResponse = stubResponse;
        _modelId = modelId;
    }

    /// <summary>
    /// Returns the pre-configured stub response as if it were produced by the model.
    /// </summary>
    /// <param name="modelId">The identifier of the model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelResponse"/> containing the stub reply.</returns>
    public Task<ModelResponse> ChatAsync(string modelId, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools = null, CancellationToken ct = default)
    {
        var response = new ModelResponse(_stubResponse, new TokenUsage(10, 20), modelId, "stop");
        return Task.FromResult(response);
    }

    /// <summary>
    /// Streams the stub response one character at a time.
    /// </summary>
    /// <param name="modelId">The identifier of the model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous stream of character chunks that compose the stub response.</returns>
    public async IAsyncEnumerable<string> ChatStreamAsync(string modelId, IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var ch in _stubResponse)
        {
            ct.ThrowIfCancellationRequested();
            yield return ch.ToString();
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Reports the health of the stub model, which is always considered available.
    /// </summary>
    /// <param name="modelId">The identifier of the model to check.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelHealth"/> indicating the stub is healthy.</returns>
    public Task<ModelHealth> GetHealthAsync(string modelId, CancellationToken ct = default)
    {
        return Task.FromResult(new ModelHealth(modelId, true, null, null));
    }
}
