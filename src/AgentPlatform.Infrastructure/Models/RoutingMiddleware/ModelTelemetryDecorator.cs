using System.Diagnostics;
using System.Runtime.CompilerServices;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Models.RoutingMiddleware;

/// <summary>
/// Decorates an <see cref="IModelClient"/> with timing and error telemetry for all model invocations.
/// </summary>
internal sealed class ModelTelemetryDecorator : IModelClient
{
    private readonly IModelClient _inner;
    private readonly ILogger<ModelTelemetryDecorator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelTelemetryDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner model client whose calls are instrumented.</param>
    /// <param name="logger">The logger used to record invocation telemetry.</param>
    public ModelTelemetryDecorator(
        IModelClient inner,
        ILogger<ModelTelemetryDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    /// <summary>
    /// Sends a chat completion request to the inner model client, recording latency and token usage telemetry.
    /// </summary>
    /// <param name="modelId">The identifier of the model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the <see cref="ModelResponse"/> returned by the inner client.</returns>
    public async Task<ModelResponse> ChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.ChatAsync(modelId, messages, tools, ct);
            sw.Stop();
            _logger.LogInformation(
                "Model {ModelId} call succeeded in {Elapsed}ms, tokens: {Tokens}",
                modelId, sw.ElapsedMilliseconds, result.TokenUsage?.TotalTokens);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Model {ModelId} call failed after {Elapsed}ms",
                modelId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Streams a chat completion response from the inner model client, recording latency and throughput telemetry.
    /// </summary>
    /// <param name="modelId">The identifier of the model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous stream of text chunks returned by the inner client.</returns>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        long totalChars = 0;
        await using var enumerator = _inner.ChatStreamAsync(modelId, messages, ct).GetAsyncEnumerator(ct);

        while (true)
        {
            string chunk;
            try
            {
                if (!await enumerator.MoveNextAsync()) break;
                chunk = enumerator.Current;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Model {ModelId} stream failed after {Elapsed}ms, chars before error: {Chars}",
                    modelId, sw.ElapsedMilliseconds, totalChars);
                throw;
            }
            totalChars += chunk.Length;
            yield return chunk;
        }

        _logger.LogInformation(
            "Model {ModelId} stream succeeded in {Elapsed}ms, chars: {Chars}",
            modelId, sw.ElapsedMilliseconds, totalChars);
    }

    /// <summary>
    /// Delegates the health check to the inner model client.
    /// </summary>
    /// <param name="modelId">The identifier of the model whose health to check.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelHealth"/> describing the inner client's availability.</returns>
    public Task<ModelHealth> GetHealthAsync(string modelId, CancellationToken ct = default)
    {
        return _inner.GetHealthAsync(modelId, ct);
    }
}
