## 附录 D：多模型统一调用机制详解

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：平台需要同时对接多个 LLM 提供商（OpenAI / Anthropic / DeepSeek / Qwen / 本地 vLLM），通过 Semantic Kernel 统一调用接口 + 自定义路由层实现智能调度。

### D.1 解决的核心问题

```
┌──────────────────────────────────────────────────┐
│ 平台需要调用的模型：                                │
│                                                  │
│  GPT-4o          (OpenAI)        商用，贵，能力强    │
│  Claude 3.5      (Anthropic)     商用，贵，能力强    │
│  DeepSeek Chat   (DeepSeek)      便宜，性能好       │
│  Qwen 2.5        (阿里通义)       便宜，中文强        │
│  本地部署模型      (vLLM)          免费，可控         │
└──────────────────────────────────────────────────┘

痛点：每个提供商的 API 格式、认证方式、限流策略、计费单位都不同。
方案：Semantic Kernel（统一怎么调）+ 路由层（统一调哪个）。
```

### D.2 Semantic Kernel：统一接口层

Semantic Kernel（SK）将不同模型统一为同一个 `IChatCompletionService` 接口：

```
┌─────────────────────────────────────────────────────────────┐
│                    你的业务代码                              │
│          var reply = await chatService.SendMessageAsync(...); │
└─────────────────────────┬───────────────────────────────────┘
                          │ 只调一个接口
                          ▼
┌─────────────────────────────────────────────────────────────┐
│               Semantic Kernel (SK)                          │
│                                                              │
│  IChatCompletionService ← 统一的聊天接口                       │
│       │                                                      │
│       ├── OpenAIConnector        → OpenAI / Azure OpenAI    │
│       ├── AnthropicConnector     → Anthropic Claude         │
│       ├── OllamaConnector        → Ollama 本地模型            │
│       └── OpenAICompatibleConnector → 任何 OpenAI 兼容 API     │
│              │                                               │
│              └── vLLM / DeepSeek / Qwen / LM Studio ...     │
│                 （它们都实现了 OpenAI 兼容接口）               │
└─────────────────────────────────────────────────────────────┘
```

关键洞察：**vLLM、DeepSeek、Qwen 等都兼容 OpenAI API 格式，SK 只需一个 `OpenAICompatibleConnector` 就能对接几乎所有模型。**

```csharp
// Infrastructure/Models/SemanticKernelModelClient.cs
public class SemanticKernelModelClient : IModelClient
{
    private readonly Dictionary<string, IChatCompletionService> _services;

    public SemanticKernelModelClient(IConfiguration config)
    {
        _services = new()
        {
            ["gpt-4o"]      = AddOpenAI("gpt-4o",      config["OpenAI:Key"]),
            ["claude-3.5"]  = AddAnthropic("claude-3.5", config["Anthropic:Key"]),
            ["deepseek"]    = AddOpenAICompatible("deepseek", config["DeepSeek:Url"], config["DeepSeek:Key"]),
            ["qwen"]        = AddOpenAICompatible("qwen",     config["Qwen:Url"],   config["Qwen:Key"]),
            ["local-llm"]   = AddOpenAICompatible("local",    config["VLLM:Url"]),  // 无需 Key
        };
    }

    // 统一调用入口——业务代码完全不关心背后是哪个模型
    public async Task<ModelResponse> ChatAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var service = _services[modelId];
        var reply = await service.GetChatMessageContentAsync(messages, cancellationToken: ct);
        return new ModelResponse(reply.Content, reply.Metadata?.Usage, modelId, reply.FinishReason);
    }
}
```

### D.3 自定义路由层：智能调度

路由层解决"调哪个模型"的决策问题（蓝图第 73 行 `ModelRouter.cs`）：

```
                  用户请求
                    │
                    ▼
          ┌─────────────────────┐
          │   路由层 Router       │  ← 在这里做决策
          │                     │
          │  决策依据：          │
          │  - 成本预算          │
          │  - 可用性            │
          │  - 延迟要求          │
          │  - 模型能力          │
          │  - 负载均衡          │
          └────────┬────────────┘
                   │ 选定一个模型
        ┌──────────┼──────────┐
        ▼          ▼          ▼
    gpt-4o    deepseek    qwen
    (主模型)   (备用1)     (备用2)
```

```csharp
// Application/Routing/Services/ModelRouter.cs
public class ModelRouter : IModelRouter
{
    private readonly IModelClient _modelClient;
    private readonly ITenantConfigRepository _configRepo;
    private readonly IAuditLogRepository _auditLog;

    public async Task<ModelResponse> RouteAsync(RoutingRequest request, CancellationToken ct)
    {
        // 1. 从租户配置获取模型路由策略
        var strategy = await _configRepo.GetRoutingStrategyAsync(request.TenantId);

        // 2. 获取模型候选列表（按优先级排序）
        var candidates = BuildCandidateList(request, strategy);

        // 3. 逐个尝试，失败自动降级
        foreach (var candidate in candidates)
        {
            try
            {
                if (!await CheckBudgetAsync(request.TenantId, candidate))
                    continue;

                var response = await _modelClient.ChatAsync(candidate.ModelId, request.Messages, ct);
                await RecordUsageAsync(request.TenantId, candidate, response.TokenUsage);
                return response;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                await RecordFallbackAsync(request.TenantId, candidate, ex);
                continue;
            }
        }

        throw new AllModelsFailedException("所有候选模型均不可用");
    }
}
```

### D.4 四大路由策略

#### 策略一：降级（Fallback）

```
正常流程：
  请求 → gpt-4o（主模型）→ ✅ 返回结果

主模型失败：
  请求 → gpt-4o（超时 30s）→ ❌ → deepseek（备用1）→ ✅ 返回结果

备用也失败：
  请求 → gpt-4o ❌ → deepseek ❌ → qwen（备用2）→ ✅ 返回结果
```

Yaml 配置（蓝图第 186 行的 Agent 配置模块，YamlDotNet 解析）：

```yaml
# config/routing-strategy.yaml
routing:
  tenant: "acme-corp"
  rules:
    - name: "default"
      candidates:                          # 按优先级排序
        - modelId: "gpt-4o"
          weight: 100
          timeout: 30s
        - modelId: "deepseek"
          weight: 80
          timeout: 20s
        - modelId: "qwen"
          weight: 60
          timeout: 20s
      fallbackPolicy: Sequential            # 顺序降级
```

降级事件（走 MediatR 领域事件）：

```csharp
public record ModelFallbackEvent(
    Guid TenantId,
    string FromModel,
    string ToModel,
    string Reason);                        // timeout / rate_limit / server_error
```

#### 策略二：重试 + 熔断（Polly）

```csharp
// 重试 + 熔断管道定义
var retryPipeline = new ResiliencePipelineBuilder<ModelResponse>()
    .AddRetry(new RetryStrategyOptions<ModelResponse>
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(500),
        BackoffType = DelayBackoffType.Exponential,  // 500ms → 1s → 2s
        ShouldHandle = new PredicateBuilder<ModelResponse>()
            .Handle<HttpRequestException>()            // 网络错误
            .Handle<TaskCanceledException>()           // 超时
            .HandleResult(r => r.IsRateLimited)       // 被限流
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<ModelResponse>
    {
        FailureRatio = 0.5,                           // 50% 失败就熔断
        MinimumThroughput = 5,                          // 至少 5 次调用后才判断
        BreakDuration = TimeSpan.FromSeconds(30),       // 熔断 30 秒
        ShouldHandle = new PredicateBuilder<ModelResponse>()
            .Handle<HttpRequestException>()
    })
    .Build();
```

```
Polly 熔断器工作原理：

正常状态 (Closed)
  │ 连续 5 次调用中失败 ≥ 3 次
  ▼
熔断状态 (Open) ── 30 秒内所有请求直接走降级，不调模型
  │ 30 秒后
  ▼
半开状态 (HalfOpen) ── 放 1 个请求试探
  │ 成功 → 回到 Closed
  │ 失败 → 回到 Open
```

#### 策略三：负载均衡（Load Balancing）

```
场景：同一模型部署了多个端点（如 3 个 vLLM 实例）

请求1 ──→ vLLM-1:8001
请求2 ──→ vLLM-2:8002
请求3 ──→ vLLM-3:8003
请求4 ──→ vLLM-1:8001  （轮询回来）
```

```yaml
# config/model-endpoints.yaml
endpoints:
  - modelId: "local-llm"
    instances:
      - name: "vllm-1"
        url: "http://gpu-node1:8001/v1"
      - name: "vllm-2"
        url: "http://gpu-node2:8002/v1"
      - name: "vllm-3"
        url: "http://gpu-node3:8003/v1"
    balanceStrategy: RoundRobin            # 轮询 / WeightedRandom / LeastConnections
```

#### 策略四：成本控制（Cost Control）

```csharp
public class CostController
{
    private readonly Money _dailyBudget;
    private Money _todaySpent;

    public async Task<bool> CanAffordAsync(ModelEndpoint endpoint, int estimatedTokens)
    {
        var costPer1kTokens = GetPricing(endpoint.Provider, endpoint.ModelName);
        var estimatedCost = new Money(costPer1kTokens.Amount * estimatedTokens / 1000);

        if (_todaySpent + estimatedCost > _dailyBudget)
            return false;  // 超预算 → 路由层强制降级到更便宜的模型

        return true;
    }
}

// 模型定价表（$ / 1M input tokens）
public static class ModelPricing
{
    private static readonly Dictionary<string, decimal> Prices = new()
    {
        ["gpt-4o"]        = 2.50m,
        ["claude-3.5"]    = 3.00m,
        ["deepseek"]      = 0.14m,     // ≈ gpt-4o 的 1/18
        ["qwen"]          = 0.40m,
        ["local-llm"]     = 0.0m,      // 本地免费
    };
}
```

### D.5 IModelClient 接口定义

> 领域层对路由层的抽象，业务代码只依赖此接口，不直接依赖 SK 或任何提供商 SDK。

```csharp
// Application/Abstractions/IModelClient.cs
public interface IModelClient
{
    /// <summary>调用指定模型（不关心具体提供商）</summary>
    Task<ModelResponse> ChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>流式调用（用于前端实时打字效果）</summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>获取模型可用状态</summary>
    Task<ModelHealth> GetHealthAsync(string modelId, CancellationToken ct = default);
}

public record ModelResponse(
    string Content,
    TokenUsage TokenUsage,
    string ModelId,
    string FinishReason);

public record ChatMessage(
    MessageRole Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null);

public record ModelHealth(
    string ModelId,
    bool IsAvailable,
    TimeSpan? AvgLatency,
    int? RateLimitRemaining);
```

### D.6 vLLM 接入方式

vLLM 原生提供 OpenAI 兼容接口，接入路径最简单：

```
┌──────────────────┐         HTTP POST /v1/chat/completions
│  C# 平台 (SK)     │ ────────────────────────────────────→ │ vLLM 容器    │
│                  │ ←─── OpenAI 兼容格式的响应 ─────────── │ localhost:8001│
└──────────────────┘                                        └──────────────┘
```

```csharp
// vLLM 注册到 SK（和 OpenAI 完全一样的代码）
var kernel = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(
        modelId: "local-llm",
        endpoint: new Uri("http://gpu-node1:8001/v1"),
        apiKey: "not-needed")                              // vLLM 无需 Key
    .Build();
```

vLLM 部署命令（Docker）：

```bash
docker run --gpus all \
  -p 8001:8000 \
  -v /data/models:/app/models \
  vllm/vllm-openai:latest \
  --model /app/models/Qwen2.5-72B \
  --host 0.0.0.0 \
  --port 8000
```

### D.7 完整调用链路全景图

```
┌──────────────────────────────────────────────────────────────────┐
│  Agent（如 Developer Agent）                                      │
│  SystemPrompt: "你是一个开发工程师..."                               │
│  持有 ModelEndpoint: { Provider: "openai", ModelName: "gpt-4o" }    │
└───────────────────────────┬──────────────────────────────────────┘
                            │ 需要调用 LLM
                            ▼
┌──────────────────────────────────────────────────────────────────┐
│  ModelRouter（自定义路由层）                                        │
│                                                                   │
│  1. 查租户路由策略 → 候选模型列表 [gpt-4o, deepseek, qwen]        │
│  2. 检查成本预算 → gpt-4o 今日已超预算 → 跳过                      │
│  3. 检查熔断器状态 → deepseek 熔断中 → 跳过                       │
│  4. 选择 qwen → 获取端点 → 开始调用                               │
└───────────────────────────┬──────────────────────────────────────┘
                            │ RouteAsync("qwen", messages)
                            ▼
┌──────────────────────────────────────────────────────────────────┐
│  SemanticKernelModelClient（SK 封装）                              │
│                                                                   │
│  1. 从 _services 字典取出 qwen 的 IChatCompletionService          │
│  2. 如果是端点池 → 轮询选一个实例 (gpu-node2:8002)                │
│  3. 调用 SK 统一接口 → HTTP POST to endpoint/v1/chat/completions  │
│  4. Polly 重试/熔断包装在 SK 调用外层                               │
└───────────────────────────┬──────────────────────────────────────┘
                            │ HTTP
                            ▼
┌──────────────────────────────────────────────────────────────────┐
│  LLM 提供商                                                       │
│                                                                   │
│  ┌─────────┐  ┌──────────┐  ┌────────┐  ┌───────────────┐       │
│  │ OpenAI  │  │ Anthropic │  │ vLLM   │  │ Qwen API      │       │
│  │ gpt-4o  │  │ claude    │  │ DeepSeek│  │ 通义千问       │       │
│  └─────────┘  └──────────┘  └────────┘  └───────────────┘       │
│                                                                   │
│  ↑ 全部通过 OpenAI 兼容格式 或 SK 原生 Connector 对接              │
└───────────────────────────┬──────────────────────────────────────┘
                            │ 响应
                            ▼
┌──────────────────────────────────────────────────────────────────┐
│  后处理                                                            │
│  1. 解析响应 → ModelResponse                                       │
│  2. 累加 TokenUsage → 成本报表                                     │
│  3. 写 AuditLog（调用记录）                                        │
│  4. 如果降级了 → 发布 ModelFallbackEvent（MediatR）                 │
└──────────────────────────────────────────────────────────────────┘
```

> **一句话总结**：Semantic Kernel 把 5 种以上 LLM 提供商统一为同一个 `IChatCompletionService` 接口，自定义路由层在它之上做智能调度：按租户策略选模型、超预算自动降级到便宜模型、超时/限流自动重试+熔断（Polly）、多实例负载均衡、按 token 单价做成本控制。业务代码只调 `IModelClient` 一个接口，完全不关心背后是哪个模型。
