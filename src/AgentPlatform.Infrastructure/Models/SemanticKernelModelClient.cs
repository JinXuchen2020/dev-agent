using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Semantic Kernel-based implementation of <see cref="IModelClient"/> that routes chat completion requests to registered OpenAI-compatible endpoints.
/// </summary>
internal sealed class SemanticKernelModelClient : IModelClient
{
    private readonly Dictionary<string, IChatCompletionService> _services;
    private readonly ILogger<SemanticKernelModelClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticKernelModelClient"/> class, registering chat completion services from configuration.
    /// </summary>
    /// <param name="configuration">The application configuration containing OpenAI and vLLM connection settings.</param>
    /// <param name="modelDefaults">The configured default model settings.</param>
    /// <param name="logger">The logger used to capture model invocation diagnostics.</param>
    public SemanticKernelModelClient(
        IConfiguration configuration,
        IOptions<ModelDefaults> modelDefaults,
        ILogger<SemanticKernelModelClient> logger)
    {
        _logger = logger;
        _services = new Dictionary<string, IChatCompletionService>();

        var openAiKey = configuration["OpenAI:Key"];
        if (!string.IsNullOrEmpty(openAiKey))
        {
            var modelName = modelDefaults.Value.ModelName;
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelName, openAiKey)
                .Build();
            var service = kernel.GetRequiredService<IChatCompletionService>();
            _services[modelName] = service;
            _services[$"openai:{modelName}"] = service;
        }

        var deepSeekKey = configuration["DeepSeek:Key"];
        if (!string.IsNullOrEmpty(deepSeekKey))
        {
            var modelName = modelDefaults.Value.ModelName;
            var apiUrl = modelDefaults.Value.ModelApiUrl;
            if (!string.IsNullOrEmpty(apiUrl))
            {
#pragma warning disable SKEXP0010
                var kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(
                        modelId: modelName,
                        endpoint: new Uri(apiUrl),
                        apiKey: deepSeekKey)
                    .Build();
#pragma warning restore SKEXP0010
                var service = kernel.GetRequiredService<IChatCompletionService>();
                _services[modelName] = service;
                _services[$"deepseek:{modelName}"] = service;
            }
        }

        var vllmUrl = configuration["VLLM:Url"];
        if (!string.IsNullOrEmpty(vllmUrl))
        {
            var vllmModel = configuration["VLLM:Model"] ?? modelDefaults.Value.ModelName ?? "local-llm";
#pragma warning disable SKEXP0010
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: vllmModel,
                    endpoint: new Uri(vllmUrl),
                    apiKey: "not-needed")
                .Build();
#pragma warning restore SKEXP0010
            var service = kernel.GetRequiredService<IChatCompletionService>();
            _services[vllmModel] = service;
            _services[$"vllm:{vllmModel}"] = service;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticKernelModelClient"/> class with a pre-populated set of chat completion services.
    /// </summary>
    /// <param name="services">A dictionary mapping model identifiers to their chat completion services.</param>
    public SemanticKernelModelClient(Dictionary<string, IChatCompletionService> services)
    {
        _services = services;
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SemanticKernelModelClient>.Instance;
    }

    private static ChatHistory ToChatHistory(IReadOnlyList<ChatMessage> messages)
    {
        var chatHistory = new ChatHistory();
        foreach (var m in messages)
        {
            chatHistory.Add(new ChatMessageContent(
                m.Role switch
                {
                    AgentPlatform.Domain.Enums.MessageRole.User => AuthorRole.User,
                    AgentPlatform.Domain.Enums.MessageRole.Agent => AuthorRole.Assistant,
                    AgentPlatform.Domain.Enums.MessageRole.System => AuthorRole.System,
                    AgentPlatform.Domain.Enums.MessageRole.Tool => AuthorRole.Tool,
                    _ => AuthorRole.User
                },
                m.Content));
        }
        return chatHistory;
    }

    /// <summary>
    /// Sends a chat completion request to the registered model and returns the response with token usage metrics.
    /// </summary>
    /// <param name="modelId">The identifier of the registered model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelResponse"/> containing the reply content and token usage.</returns>
    public async Task<ModelResponse> ChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        if (!_services.TryGetValue(modelId, out var service))
            throw new ArgumentException($"Model '{modelId}' not registered", nameof(modelId));

        var chatHistory = ToChatHistory(messages);
        var reply = await service.GetChatMessageContentAsync(chatHistory, cancellationToken: ct);

        var promptTokens = 0;
        var completionTokens = 0;
        if (reply.Metadata != null && reply.Metadata.TryGetValue("Usage", out var usageObj) && usageObj != null)
        {
            try
            {
                var usageJson = JsonSerializer.Serialize(usageObj);
                using var doc = JsonDocument.Parse(usageJson);
                promptTokens = doc.RootElement.TryGetProperty("PromptTokens", out var p) ? p.GetInt32() :
                               doc.RootElement.TryGetProperty("InputTokens", out var i) ? i.GetInt32() : 0;
                completionTokens = doc.RootElement.TryGetProperty("CompletionTokens", out var c) ? c.GetInt32() :
                                  doc.RootElement.TryGetProperty("OutputTokens", out var o) ? o.GetInt32() : 0;
            }
            catch (JsonException)
            {
                // Usage metadata in unexpected format; silently fall back to zero
            }
        }

        return new ModelResponse(
            reply.Content ?? string.Empty,
            new TokenUsage(promptTokens, completionTokens),
            modelId,
            "stop");
    }

    /// <summary>
    /// Streams a chat completion response from the registered model chunk by chunk.
    /// </summary>
    /// <param name="modelId">The identifier of the registered model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous stream of text chunks returned by the model.</returns>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_services.TryGetValue(modelId, out var service))
            throw new ArgumentException($"Model '{modelId}' not registered", nameof(modelId));

        var chatHistory = ToChatHistory(messages);
        var chunks = service.GetStreamingChatMessageContentsAsync(chatHistory, cancellationToken: ct);
        await foreach (var chunk in chunks)
        {
            yield return chunk.Content ?? string.Empty;
        }
    }

    /// <summary>
    /// Reports whether the specified model is registered and available for invocation.
    /// </summary>
    /// <param name="modelId">The identifier of the model whose health to check.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelHealth"/> indicating availability.</returns>
    public Task<ModelHealth> GetHealthAsync(string modelId, CancellationToken ct = default)
    {
        var available = _services.ContainsKey(modelId);
        return Task.FromResult(new ModelHealth(modelId, available, null, null));
    }
}
