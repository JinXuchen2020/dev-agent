# F6 · Research Agent（联网多步调研）

> 设计文档（features/ 设计枢纽）。本 feature 为 🔴高风险（Phase 6 行动层）：引入外部搜索 API（密钥/限流）、新增 HTTP 路由 `POST /api/v1/research`、多步链上下文膨胀须走统一压缩。按 feature-builder 护栏属「先出设计 + 选项，停下问人确认」类，本文档为提案；关键范围决策见 §7，须用户确认后方可动手。

## §1 目标
让平台具备「开放问题 → 多步联网搜索 → 结构化报告」的调研能力（Research Agent）。核心交付：
- **真实联网检索**：调研过程对外部搜索 API 发起**真实 HTTP 调用**（非伪造），解析真实返回片段（标题/URL/摘要）。
- **多步链真实串联**：LLM 规划搜索查询 → 真实检索 → 累积发现 → LLM 综合成结构化报告；多轮上下文走统一预算压缩，避免无限膨胀。
- 前后端一体：新增 `ResearchPage` 让用户提问并看到多步进度与最终报告。

## §2 现状核验（已读真实代码，非臆测）
- **LLM 抽象**：`IModelClient`（`Application.Abstractions/IModelClient.cs`）——`ChatAsync(modelId, messages, ct)` / `ChatStreamAsync(...)` / `GetHealthAsync(...)`；配套 `ChatMessage(Role,Content,ToolCallId?,ToolName?)`、`ModelResponse(Content,TokenUsage?,ModelId,FinishReason)`、`MessageRole` 枚举。唯一实现 `SemanticKernelModelClient`（基于 Microsoft.SemanticKernel 1.30.0）；`ModelClient:Provider=Stub` 时注入 `StubModelClient`（测试替身）。`AgentCallStepExecutor`/`CriticStepExecutor` 已复用此抽象。
- **工具/HTTP 真实执行（F5）**：`NativeToolExecutor` 已真实 HTTP（IHttpClientFactory），但**不负责鉴权/响应解析**；`ToolDefinition.EndpointUrl` 是**聚合根持久化字段**（落库）——把搜索密钥拼进 URL 会落库，不合适。结论：搜索走**专用 `ISearchProvider`**，密钥走配置/环境变量，不落库。
- **编排器局限**：`SequentialOrchestrator` 的"循环"是跨多个已命名 step 的顺序执行，单 step 只一次 LLM 调用；无"agent 在单步内自我循环调工具"语义。结论：F6 做成 **Application 命令处理器**（`ResearchCommandHandler`），内部用 `for` 自驱 plan→search→synthesize，而非套工作流 step。
- **上下文压缩设施**：`ITokenCounter.CountTokens`（Infrastructure，已用于 `SequentialOrchestrator` 预算截断）+ `StringHelpers.Truncate`（Infrastructure StringHelpers）。可复用做"多轮检索发现"的总量预算。
- **WorkflowContext.Blackboard 不可变且每步重建**（不跨循环保留），故 F6 在处理器内用**本地 `List<SearchFinding>`** 累积，不依赖 WorkflowContext 跨步传递。
- **端点范式**：MediatR 自动发现（`DependencyInjection.cs:24` `RegisterServicesFromAssembly`），新增 `ResearchCommand`+`ResearchCommandHandler` 自动注册；`ConversationsController.SendMessage` 用 `[Authorize]`（全认证租户用户）+ MediatR 分派。`appsettings.json` 有 `Sandbox`/`Rag` 等 `IOptions<T>` 段。
- **前端**：`api.ts` 单 axios 实例 `baseURL:'/api/v1'`、`withCredentials:true`；页面 `lazy` 挂 `<ProtectedRoute><AppLayout>`；侧栏 `menuItems` 数组加一项即出菜单。

## §3 拟改接口契约（后端）
### 3.1 搜索提供方（真实 HTTP）
- 新增 `ISearchProvider`（`Application.Abstractions`）：
  ```csharp
  Task<SearchResult> SearchAsync(string query, int maxResults, CancellationToken ct = default);
  ```
  - `SearchResult` record：`bool Success`、`string? ErrorMessage`、`IReadOnlyList<SearchSnippet> Snippets`。
  - `SearchSnippet` record：`string Title`、`string Url`、`string Snippet`。
- 新增 `SerpApiSearchProvider`（`Infrastructure`）：`IHttpClientFactory` 真实 GET `https://serpapi.com/search.json?engine=google&q={q}&api_key={key}&num={n}`；解析 `organic_results` → `SearchSnippet`；超时取 `SearchSettings.TimeoutSeconds`（默认 15s，按请求级 `CancellationTokenSource` 链接调用方 `ct`）；非 2xx / 超时 / 缺密钥 → `Success=false` + 真实 `ErrorMessage`（**不伪造**）。
- 新增 `SearchSettings`（`Application.Abstractions`，POCO + `IOptions`）：`Provider`(默认 "SerpApi")、`SerpApiKey`(默认空，生产走环境变量 `Search__SerpApiKey`)、`BaseUrl`(默认 serpapi)、`TimeoutSeconds`(默认 15)、`DefaultMaxResults`(默认 5)。
- DI：`services.Configure<SearchSettings>(configuration.GetSection("Search"));` + 注册 `ISearchProvider` → `SerpApiSearchProvider`（Scoped，复用 F5 已加的 `AddHttpClient()`）。`appsettings.json` 加 `Search` 节（Key 留空 + 注释"生产由环境变量覆盖"）。

### 3.2 调研编排（多步链）
- 新增 `ResearchCommand`（`Application.Commands` 或 `Features.Research`）：`string Question`、`int? MaxSteps`(默认 3)、`string? ModelId`、`string? FocusInstructions`。
- 新增 `ResearchCommandHandler`（`Application.Research`）：注入 `IModelClient`、`ISearchProvider`、`ITokenCounter`、`IOptions<StateMachineSettings>`(取 `MaxSummaryTokens` 作压缩预算)、`IOptions<SearchSettings>`(取 `DefaultMaxResults`)、`ILogger`。研究过程不触碰租户持久化数据，故**不注入 `ITenantProvider`**。
  - **Plan 步**：`IModelClient.ChatAsync(modelId, [system:调研规划提示, user: Question])`，要求模型输出 JSON 数组的搜索查询（≤ MaxSteps）。解析 `string[]` 查询（解析失败回退为单次"Question"查询）。
  - **Search 步**（循环每个查询）：`ISearchProvider.SearchAsync(q, maxResults, ct)` → 片段存入本地 `List<SearchFinding>(Query, Snippets)`；用 `ITokenCounter` 累计 token，超 `MaxSummaryTokens` 预算则按 FIFO 丢弃最旧片段（`StringHelpers.Truncate` 兜底）。
  - **Synthesize 步**：`IModelClient.ChatAsync(modelId, [system:综合报告提示(含累积片段+问题+FocusInstructions), user: Question])` → 模型输出 Markdown 报告（建议含 `## 来源` 与若干 `## 小节`）；解析为 `ResearchReport`。
  - 失败精准回打：搜索全失败 / LLM 抛错 → 抛 `InvalidOperationException` 带真实原因（统一异常处理返回 5xx/4xx，不静默假成功）。
- 返回 `ResearchReport`（`Application` 或 `Api.Models`）：`string Question`、`IReadOnlyList<string> SearchQueries`、`IReadOnlyList<ResearchSource> Sources`(Title,Url,Snippet)、`string Answer`(Markdown)、`IReadOnlyList<ResearchSection> Sections`(Heading,Body)、`int StepsUsed`、`TokenUsage? TokenUsage`、`DateTime GeneratedAt`。

### 3.3 端点与 DTO（Api）
- 新增 `ResearchController`：`[Route("api/v1/research")]`、`[ApiController]`、`[Authorize]`（全认证租户用户，对齐 `SendMessage`）、`[HttpPost]` 分派 `ResearchCommand`；以 **SSE**（`text/event-stream`）流式写出 `ResearchProgressEvent`（`Plan` → `SearchStart`/`SearchDone`×N → `Synthesize` → `Report`，异常为 `Error` 后补 `Report`）；终端帧 `event: done` 关闭流。序列化用 `JsonNamingPolicy.CamelCase`，`Type` 为整型枚举（0–5）。
- `ResearchRequest`（Models）：`string Question`(`[Required]`)、`int? MaxSteps`、`string? ModelId`、`string? FocusInstructions`（camelCase）。
- 前端 `ResearchPage` 用 `fetch` + `credentials:'include'` 消费 SSE（EventSource 仅支持 GET），逐帧解析 `data:` JSON 渲染进度与最终结构化报告（`ResearchReport` 的 `Sources`/`Answer`/`Sections`）。

## §4 数据模型
- **不新增表 / 聚合 / EF 迁移**：纯服务 + 配置（`SearchSettings` POCO，无持久化）。`ResearchReport` 为运行时 DTO。

## §5 验收标准
- **真实联网检索（A 类）**：`SerpApiSearchProvider` 对 SerpAPI 发真实 GET（URL/查询参数/key 注入正确）；用 `StubHttpMessageHandler`（镜像 `NativeToolExecutorTests`）模拟 SerpAPI 返回 → 断言请求构造 + 片段解析正确；缺 key / 非 2xx / 超时 → `Success=false` + 真实错误。
- **多步链真实串联（B 类）**：`ResearchCommandHandler` 用 NSubstitute 脚本化 `IModelClient`（plan 轮返回 JSON 查询数组、synthesize 轮返回 Markdown）+ 脚本化 `ISearchProvider`（返回片段）→ 断言：调用搜索 **N 次**（N=规划查询数，≤MaxSteps）、`SearchQueries`/`Sources` 正确、`Sections`/`Answer` 非空、`StepsUsed`=N。
- **QA**：`dotnet build` 0 error/warning；`dotnet test` 全绿；前端 `tsc --noEmit` 0 error + `node scripts/qa.mjs`（typecheck/lint/build/unit）全绿。
- **真实副作用说明**：搜索 HTTP 路径经 `HttpClient`（非伪造），由 mock transport 证明真实请求发出+响应回填（对齐 F5 A1 验收范式）；LLM 调用经注入 `IModelClient`（测试中用 stub，生产中为真实 SemanticKernel OpenAI/DeepSeek）。外部 API「真实调用」验收 = 搜索提供方真实 HTTP 路径已覆盖。

## §6 质量门清单（嵌入本设计文档，Phase 5 消费）
- **P0（阻断）**：
  - 搜索提供方真实 HTTP 调用 + 真实响应解析；缺 key/非 2xx/超时 → 失败精准回打，绝不伪造成功。
  - 多步链真实串联（plan→search×N→synthesize），非单次伪造；累积发现经预算压缩。
  - 搜索密钥**不落库**（走 `SearchSettings`/环境变量，不进 `ToolDefinition.EndpointUrl`）。
- **P1（高）**：
  - 新增接口（`ISearchProvider`/`SearchResult`/`ResearchReport` 等）契约稳定；`IModelClient` 零改动（仅复用）。
  - 配置经 `IOptions<SearchSettings>` 注入，不写死密钥/URL。
  - 端点 RBAC 与现有 `SendMessage` 一致（或按 §7 决策收紧）。
- **P2（中）**：
  - 真实路径结构化日志（查询脱敏、结果数、耗时、token）。
  - 单测覆盖：搜索真实 HTTP 路径（成功/失败/超时/缺 key）+ 多步循环（规划/检索/综合/预算截断）。
- **P3（低）**：
  - `ResearchReport` 字段前端类型一一对应（`types/index.ts`），无 `any`。
  - 死代码/空 catch 清理；`appsettings.json` 加 `Search` 节注释。

## §7 风险与范围决策（✅已确认）
F6 为高风险：外部 API（密钥/限流）+ 新路由 + 多步链压缩。决策已通过 AskUserQuestion 确认：
- **S1 搜索提供方**：**SerpApi**（真实 HTTP 已实现路径，密钥走 `SearchSettings`/环境变量 `Search__SerpApiKey`，不落库）。`Provider` 字段保留为未来多提供方扩展点（DI 已按 `Provider` 选择实现，未知值启动即报错）。
- **S2 前端范围**：**新增 `ResearchPage`**（真实可用 UI，含提问/实时进度 Timeline/结构化报告渲染）。
- **S3 响应模式**：**SSE 流式多步进度**（前端 `fetch` + `credentials:'include'` 消费，体验优先）。
- **S4 RBAC**：`[Authorize]` **全认证租户用户**（对齐 `SendMessage`，不做成仅 Admin/Operator）。
- 其余默认：MaxSteps=3（clamp 1–8）、压缩预算=`StateMachineSettings.MaxSummaryTokens`(8000)、超时=`SearchSettings.TimeoutSeconds`(15s)。

> 另：F6 不做成工作流节点（避免套用 step 循环语义），作为一等 `POST /api/v1/research` 能力；未来可在 F7 平台化中把它暴露为 Tool/Agent。

## §8 质量门记录（实现后填）
- 8.1 **ddd-code-reviewer：PASS（0 open）**。对抗式审查覆盖 Section C（Orchestrator）+ G（Controller）+ H（Config）+ H2（SSE 资源生命周期）+ Z（General）。已修复：① `SearchSettings.Provider` 原未被消费 → DI 改为按 `Provider` 选择实现（未知值启动报错，消除静默失败）；② `ResearchRequest.Question` 增加 `[Required]` 使空问题返回 400（原手动抛 `ArgumentException`→500）；③ 删除零引用 `SearchResult.None` 单例（死代码 P1）。核对真实副作用：搜索 HTTP 路径由 `SerpApiSearchProviderTests`（StubHttpMessageHandler 模拟 SerpAPI）→ 断言真实 GET 构造 + `organic_results` 解析；多步链由 `ResearchCommandHandlerTests`（NSubstitute 脚本化 `IModelClient`/`ISearchProvider`）→ 断言搜索调用 N 次、`Sources` 去重累积、`Sections`/`Answer` 非空、`StepsUsed`=N；计划/综合失败时精准回打 `Error`+空 `Report`，绝不伪造成功。
- 8.2 **ddd-phase-quality-gate：PASS（P0=P1=P2=P3=0）**。12 类审计全跑：DI 注册完整性（`ISearchProvider` 已注册）、DDD 分层（接口在 Abstractions / 实现在 Infrastructure / DI 在 Infrastructure）、无 EF 迁移需求、硬编码值均为 API 参数（可配）、CancellationToken 全链路透传、实现类 `internal sealed`、无 Singleton 增长型集合、空问题已守卫、`[ApiController]` 自动校验、`SearchSettings` 经 `IOptions<T>`、新增枚举/常量无零引用（删除 `None` 后）、XML 中文注释齐备、Swagger 沿用既有全局配置。
- 8.3 **codebase-optimizer：PASS（0 open）**。多轮优化：消除死代码（`SearchResult.None`）、消除未消费配置（`Provider` 接线上线）、空问题输入校验前移、C# 迭代器规避 `yield in try`（Safe* 隔离）、NSubstitute 歧义修正（`Returns<Task<ModelResponse>>`）。构建 0 warning / 0 error，全方案 238 测试全绿。
