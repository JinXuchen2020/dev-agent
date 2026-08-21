using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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

    /// <summary>
    /// Builds a <see cref="SemanticKernelModelClient"/> from explicit tenant credentials, enabling per-tenant
    /// model isolation. Shared with the global configuration path so both register identical service keys.
    /// </summary>
    /// <param name="apiKey">The decrypted API key for the tenant's provider.</param>
    /// <param name="baseUrl">Optional OpenAI-compatible base URL (DeepSeek / VLLM / Custom); null uses the default endpoint.</param>
    /// <param name="modelName">The model name to register (also registered as <c>provider:modelName</c>).</param>
    /// <param name="provider">The normalized provider key used in the registered service id.</param>
    public static SemanticKernelModelClient CreateForTenant(
        string apiKey,
        string? baseUrl,
        string modelName,
        string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var services = new Dictionary<string, IChatCompletionService>();
        var builder = Kernel.CreateBuilder();
        if (!string.IsNullOrEmpty(baseUrl))
        {
#pragma warning disable SKEXP0010
            builder.AddOpenAIChatCompletion(modelName, new Uri(baseUrl), apiKey);
#pragma warning restore SKEXP0010
        }
        else
        {
            builder.AddOpenAIChatCompletion(modelName, apiKey);
        }

        var service = builder.Build().GetRequiredService<IChatCompletionService>();
        services[modelName] = service;
        services[$"{provider}:{modelName}"] = service;

        return new SemanticKernelModelClient(services);
    }

    private static ChatHistory ToChatHistory(IReadOnlyList<ChatMessage> messages)
    {
        var chatHistory = new ChatHistory();
        foreach (var m in messages)
        {
            // Assistant message that proposed tool calls on a previous turn: echo them back so the
            // model sees its own tool_calls (OpenAI requires tool messages to be preceded by tool_calls).
            // SK 1.30 ctor: FunctionCallContent(functionName, pluginName, id, arguments).
            if (m.ToolCalls is { Count: > 0 } && m.Role == AgentPlatform.Domain.Enums.MessageRole.Agent)
            {
                var items = new ChatMessageContentItemCollection();
                foreach (var call in m.ToolCalls)
                {
                    KernelArguments? args = null;
                    if (!string.IsNullOrWhiteSpace(call.ArgumentsJson) && call.ArgumentsJson != "{}")
                    {
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson);
                            if (dict is not null) args = new KernelArguments(dict);
                        }
                        catch (JsonException)
                        {
                            // fall back to no arguments
                        }
                    }
                    items.Add(new FunctionCallContent(functionName: call.Name, id: call.Id, arguments: args));
                }
                chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, items: items));
                continue;
            }

            // Tool result message: pair it to the originating assistant tool call.
            // SK 1.30 ctor: FunctionResultContent(functionName, pluginName, callId, result).
            if (m.Role == AgentPlatform.Domain.Enums.MessageRole.Tool && !string.IsNullOrEmpty(m.ToolCallId))
            {
                var toolItems = new ChatMessageContentItemCollection
                {
                    new FunctionResultContent(functionName: m.ToolName, callId: m.ToolCallId!, result: m.Content)
                };
                chatHistory.Add(new ChatMessageContent(AuthorRole.Tool, items: toolItems));
                continue;
            }

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

    private static OpenAIFunction ToOpenAIFunction(ToolDefinition tool)
    {
        // SK 1.30 declares tools via KernelFunctionMetadata → OpenAIFunction (OpenAIFunction's own
        // constructor is internal). We mirror the tool's JSON parameter schema into parameter metadata
        // so the model sees the real argument contract.
        var schema = string.IsNullOrWhiteSpace(tool.ParametersSchema) ? "{}" : tool.ParametersSchema;
        var parameters = new List<KernelParameterMetadata>();
        try
        {
            using var doc = JsonDocument.Parse(schema);
            if (doc.RootElement.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (doc.RootElement.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                    foreach (var r in req.EnumerateArray())
                        if (r.ValueKind == JsonValueKind.String)
                            required.Add(r.GetString()!);

                foreach (var prop in props.EnumerateObject())
                {
                    string? description = null;
                    if (prop.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                        description = d.GetString();

                    parameters.Add(new KernelParameterMetadata(prop.Name)
                    {
                        Description = description,
                        IsRequired = required.Contains(prop.Name)
                    });
                }
            }
        }
        catch (JsonException)
        {
            // Parameters schema is optional; declare the function without parameter metadata.
        }

        var metadata = new KernelFunctionMetadata(tool.Name)
        {
            Description = tool.Description ?? string.Empty,
            Parameters = parameters
        };
        return metadata.ToOpenAIFunction();
    }

    private static string SerializeArguments(KernelArguments? args)
    {
        if (args is null) return "{}";
        if (args is IDictionary<string, object?> dict)
            return JsonSerializer.Serialize(dict);
        return JsonSerializer.Serialize(args);
    }

    /// <summary>
    /// Sends a chat completion request to the registered model and returns the response with token usage metrics.
    /// When <paramref name="tools"/> is supplied the model may propose tool calls (declared only — execution
    /// stays with the caller via <see cref="ToolCallBehavior.EnableFunctions"/> with auto-invoke disabled).
    /// </summary>
    /// <param name="modelId">The identifier of the registered model to invoke.</param>
    /// <param name="messages">The conversation history to send to the model.</param>
    /// <param name="tools">Optional tool definitions the model is allowed to invoke.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="ModelResponse"/> containing the reply content and token usage.</returns>
    public async Task<ModelResponse> ChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        if (!_services.TryGetValue(modelId, out var service))
            throw new ArgumentException($"Model '{modelId}' not registered", nameof(modelId));

        var sw = Stopwatch.StartNew();
        var provider = modelId.Contains(':') ? modelId.Split(':')[0] : "unknown";
        var modelName = modelId.Contains(':') ? modelId.Split(':')[1] : modelId;

        var chatHistory = ToChatHistory(messages);

        OpenAIPromptExecutionSettings? settings = null;
        if (tools is { Count: > 0 })
        {
            settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.EnableFunctions(tools.Select(ToOpenAIFunction), autoInvoke: false)
            };
        }

        var reply = settings is null
            ? await service.GetChatMessageContentAsync(chatHistory, cancellationToken: ct)
            : await service.GetChatMessageContentAsync(chatHistory, settings, kernel: null, cancellationToken: ct);
        sw.Stop();

        // Record model call metrics
        WorkflowMetrics.ModelCallCounter.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", modelName));
        WorkflowMetrics.ModelCallDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", modelName));

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

        var calls = reply.Items.OfType<FunctionCallContent>()
            .Select(c => new ToolCall(c.Id ?? string.Empty, c.FunctionName, SerializeArguments(c.Arguments)))
            .ToList();

        var finishReason = calls.Count > 0 ? "tool_calls" : "stop";

        return new ModelResponse(
            reply.Content ?? string.Empty,
            new TokenUsage(promptTokens, completionTokens),
            modelId,
            finishReason,
            calls.Count > 0 ? calls : null);
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
