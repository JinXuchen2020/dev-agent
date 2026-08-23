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
    // Shared, long-lived HttpClient whose handler chain rewrites tool-call arguments into the
    // JSON-object shape Agnes requires (see OpenAIArgumentsNormalizer). Reused across every service
    // this client builds, so we never exhaust sockets.
    private static readonly HttpClient SharedHttpClient =
        new(new OpenAIArgumentsNormalizer(new HttpClientHandler()), disposeHandler: false);

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
            var service = BuildService(modelName, null, openAiKey);
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
                var service = BuildService(modelName, apiUrl, deepSeekKey);
                _services[modelName] = service;
                _services[$"deepseek:{modelName}"] = service;
            }
        }

        var vllmUrl = configuration["VLLM:Url"];
        if (!string.IsNullOrEmpty(vllmUrl))
        {
            var vllmModel = configuration["VLLM:Model"] ?? modelDefaults.Value.ModelName ?? "local-llm";
            var service = BuildService(vllmModel, vllmUrl, "not-needed");
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
        var service = BuildService(modelName, baseUrl, apiKey);
        services[modelName] = service;
        services[$"{provider}:{modelName}"] = service;

        return new SemanticKernelModelClient(services);
    }

    /// <summary>
    /// Builds an <see cref="IChatCompletionService"/> backed by the shared HttpClient whose handler
    /// chain normalizes tool-call arguments for Agnes. Uses the public
    /// <c>OpenAIChatCompletionService</c> constructor so the custom HttpClient can be injected directly —
    /// <c>AddOpenAIChatCompletion</c> would otherwise spin up its own HttpClient and bypass the normalizer.
    /// </summary>
    private static IChatCompletionService BuildService(string modelName, string? baseUrl, string apiKey)
    {
#pragma warning disable SKEXP0010
        if (!string.IsNullOrEmpty(baseUrl))
        {
            return new OpenAIChatCompletionService(
                modelId: modelName,
                endpoint: new Uri(baseUrl),
                apiKey: apiKey,
                organization: null,
                httpClient: SharedHttpClient,
                loggerFactory: null);
        }

        return new OpenAIChatCompletionService(
            modelId: modelName,
            apiKey: apiKey,
            organization: null,
            httpClient: SharedHttpClient,
            loggerFactory: null);
#pragma warning restore SKEXP0010
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
                            // 反序列化为原生 CLR 类型（string/number/bool/null/dict），不要保留 JsonElement。
                            // JsonSerializer.Deserialize<Dictionary<string, object?>> 会把标量值解析成 JsonElement，
                            // 而 KernelArguments 持有 JsonElement 时 SK 序列化出的 arguments 不是标准 JSON object，
                            // 导致 Agnes（OpenAI 兼容）报 "arguments must be a JSON object" 400。
                            var raw = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson);
                            if (raw.ValueKind == JsonValueKind.Object)
                            {
                                var native = ToNativeObject(raw) as Dictionary<string, object?>;
                                if (native is not null) args = new KernelArguments(native);
                            }
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
    /// Recursively converts a <see cref="JsonElement"/> graph into native CLR types
    /// (string / JsonElement-less numbers / bool / null / nested dictionaries / lists) so that
    /// <see cref="KernelArguments"/> holds plain values. This keeps SK's tool-call argument
    /// serialization standard when sent back to OpenAI-compatible endpoints (e.g. Agnes).
    /// </summary>
    private static object? ToNativeObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var i)) return i;
                if (element.TryGetInt64(out var l)) return l;
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = ToNativeObject(prop.Value);
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(ToNativeObject(item));
                return list;
            default:
                return null;
        }
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
        {
            // 未配置任何 provider（全局 OpenAI/DeepSeek/VLLM 全空，且当前租户无对应 BYO 凭据）
            // 时，抛出明确错误而非静默返回模拟回复。
            if (_services.Count == 0)
                throw new InvalidOperationException(
                    $"未配置任何模型 provider，无法调用模型 '{modelId}'。请配置平台级 LLM 端点 " +
                    "(OpenAI:Key / DeepSeek:Key / VLLM:Url) 或在「我的凭据」中添加 BYO 模型凭据。");
            throw new ArgumentException($"Model '{modelId}' not registered", nameof(modelId));
        }

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
        {
            // 未配置任何 provider（全局 OpenAI/DeepSeek/VLLM 全空，且当前租户无对应 BYO 凭据）
            // 时，抛出明确错误而非静默返回模拟回复。
            if (_services.Count == 0)
                throw new InvalidOperationException(
                    $"未配置任何模型 provider，无法调用模型 '{modelId}'。请配置平台级 LLM 端点 " +
                    "(OpenAI:Key / DeepSeek:Key / VLLM:Url) 或在「我的凭据」中添加 BYO 模型凭据。");
            throw new ArgumentException($"Model '{modelId}' not registered", nameof(modelId));
        }

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
