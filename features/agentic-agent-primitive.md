# F29 · Agentic Agent Primitive（自主 Agent 控制循环原语）  [P0 · 置顶]

> **定位**：二期第一个 feature，最高优先级，独立轨道（拟 Phase 12），先于 Phase 7–11 启动。
> **来源**：用户 2026-08-12 拍板「单 agent 可升级为 Codex 式自主 agent，且应是二期第一个 feature，需置顶」。
> **设计依据**：本文件 + `docs/agent-harness-blueprint.md`（Phase 7–11 现状核实）；代码事实均来自 2026-08-12 直接核实。

---

## 0. 为什么置顶 / 为什么是独立轨道

- **差距根因（范式级）**：dev-agent 现在是「可视化 DAG 工作流引擎 + 多租户后端」——控制流由人在 UI 画死，LLM 只是节点内的无状态函数（且 `AgentCallStepExecutor` 还硬编码 prompt + `DefaultModelId`，连 agent 自己的 `SystemPrompt`/`ModelEndpoint` 都不读）。真 harness（Codex / Claude Code / WorkBuddy）的本质是 **LLM 自主控制循环**：`plan → act → observe → reflect`，循环由模型驾驶。
- **这是产品差异化核心**：二期其余项（Phase 7–11 = F30 执行持久化 / F31 Agent 实体化 / F32 消息总线 / F33 语义记忆 / F34 在线评估）把引擎做生产级，但**不引入 agentic control loop 原语**——所以即便二期全做完，dev-agent 仍是「生产级多租户 agent 工作流 PaaS」，**不是** Codex 式自主编码 agent。本 feature 补的就是那个范式缺口。
- **单 agent 就是正确单元**：Codex / Claude Code 本质 = 一个自主 agent + 一套工具。多 agent 协作（F32）是可选增值，**不是**自主的前提。因此不必先搞多 agent，先把单 agent 装上控制循环原语即可。
- **地基已埋 ~65%**（代码核实，见 §1），本 feature 是把「模型驾驶位」接起来的剩余 ~35%，且都不是研究问题，全是工程接线。

---

## 1. 现状核实（Verified Current State · 2026-08-12）

### 1.1 已具备（可直接复用）

| 模块 | 现状 | 说明 |
|---|---|---|
| 工具**执行**半边 | ✅ `ToolCallingDispatcher`（`Application/Tools/`）+ `IToolRegistry` + 3 个 `IToolExecutor`（Native / Skill / MCP） | 工具的「执行」已齐，缺「模型驾驶位」调度 |
| 上下文数据模型 | ✅ `ChatMessage` 已含 `ToolCallId` / `ToolName` 字段 | 已预留工具回合的 message 结构 |
| 模型底层 | ✅ `SemanticKernelModelClient` 基于 Semantic Kernel | **SK 原生支持 function calling**，只是没接线 |
| 模型路由 | 🟡 `ModelRouter` + `TenantModelClientResolver` 已存在（F13） | per-agent 模型只差接线 |
| 流式 | ✅ `IModelClient.ChatStreamAsync` 已存在 | 长程可中断 UX 的底座 |
| 多租户 / HITL / Trace | ✅ 企业级已落地（Phase 5 / F20 / F24） | 自主 agent 直接继承隔离、审计、可中断 |
| 代码沙箱 substrate | ✅ F9(Docker) / F10(Skill·MCP) / F11(OS 隔离) / F34(双层) | ④ workspace 工具的底层执行环境 |

### 1.2 待新建（真正的工程量，P0 优先）

| 缺口 | 内容 | 分级 | 难度 |
|---|---|---|---|
| **① 模型工具调用通道** | `IModelClient.ChatAsync` 现**仅返回纯文本** `ModelResponse(Content, TokenUsage, ModelId, FinishReason)`，**无 ToolCalls**——这是 Codex 式自主的**最大 blocker**。`SemanticKernelModelClient` 只 `GetChatMessageContentAsync` 且只读 `reply.Content`，未注册 function、未解析 `ToolCallContent` | 🔴 P0 | 中（SK 原生支持，纯接线） |
| **② ReAct 控制循环引擎** | 新 `AgenticOrchestrator` / `AgentLoopService`：目标 + agent 配置 + 允许工具 → 循环「组消息(系统 prompt + 工具说明) → 调模型 → 有 tool call 就 `ToolCallingDispatcher.DispatchAsync` → 结果作为 Tool message 回灌 → 无 tool call 则判停」+ 迭代硬上限 | 🔴 P0 | 中 |
| **③ agent 配置字段** | 每个 agent 的「允许工具白名单 / 最大迭代 / 停止判定」。需迁移（与 F31 协同；F31 本就要补种子） | 🔴 P0 | 低 |
| **④ agent workspace / FS 工具** | 当前工具源仅 Native/Skill/MCP，**没有「读/写/改文件 + 跑命令」的 workspace 工具**——这是「Codex 式 coding 自主」的硬前提。可用现有 Code/Tool 沙箱（F9/F10/F11/F34）包一层 | 🔴 P0 | 中 |
| **⑤ 安全护栏** | 路径白名单、命令黑名单、破坏性操作确认、硬成本/迭代上限。无护栏的自驱 agent 危险 | 🔴 P0 | 低–中 |
| ⑥ durable 长程 | agent 任务 = 分钟级/数百步；当前 `SequentialOrchestrator` **单 HTTP 请求同步跑完**会超时/崩溃丢状态（= F30 旧号 Durable） | 🟡 P1 | 中 |
| ⑦ 流式可中断 UX | 把思考/工具调用实时推前端 + 中途插指令（复用 `ChatStreamAsync` + 现有 SSE 鉴权） | 🟡 P1 | 中 |
| ⑧ compaction | 长程撑爆上下文 → 语义摘要（部分 = F33 语义记忆） | 🟡 P1 | 中 |

---

## 2. 目标

把「agent 配置实体」升级为「真 agent」：**给定目标 + 允许工具白名单**，模型自主循环决策、调用工具、观察结果、再决策，直到停止条件。让单 agent 具备 Codex 式自主完成任务的能力。

---

## 3. 核心改造（P0 优先）

### 3.1 ① 模型工具调用通道（最大 blocker）

- 扩 `IModelClient.ChatAsync`：新增可选参数 `IReadOnlyList<ToolDefinition>? tools`；`ModelResponse` 增 `IReadOnlyList<ToolCall>? ToolCalls`。
- 新增 `ToolCall` record：`(string Id, string Name, string ArgumentsJson)`。
- `SemanticKernelModelClient`：注册 tools 到 kernel → `GetChatMessageContentAsync(..., new OpenAIChatPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.NoKernelFunctions })`（**声明但不自调**，执行权留平台）→ 解析 `reply.Items.OfType<ToolCallContent>()` → 映射为 `ToolCall`。⚠️ 切勿用 `ToolCallBehavior.AutoInvokeKernelFunctions`（会使 SK 自调，违背 agentic 本质）。详见 §11.1 Step 2。
- 保留 `ChatStreamAsync` 文本流（思考 token 流式）；工具调用走非流 `ChatAsync`。
- **单测**：mock `IChatCompletionService` 验证 function 注册 + `ToolCallContent` 解析 → `ModelResponse.ToolCalls` 非空。

### 3.2 ② ReAct 控制循环引擎（新 `AgenticOrchestrator`）

- 输入：`goal(string)` + `agent 配置`（`SystemPrompt` / `ModelEndpoint` / `AllowedToolNames` / `MaxIterations` / `StopCriteria`）+ `tenantId`。
- 伪代码：

  ```csharp
  var messages = new List<ChatMessage>
  {
      new(Role.System, agent.SystemPrompt + "\n\n可用工具：\n" + Describe(allowedTools)
          + "\n停止约定：当你认为任务已完成、无需再调用工具时，直接输出最终答案。"),
      new(Role.User, goal)
  };

  for (var i = 1; i <= agent.MaxIterations; i++)
  {
      var resp = await modelClient.ChatAsync(modelId, messages, tools: allowedToolDefs, ct);
      if (resp.ToolCalls is null or { Count: 0 })   // 模型认为任务完成
          return resp.Content;                       // 终态

      foreach (var call in resp.ToolCalls)
      {
          messages.Add(new(Role.Assistant, "", ToolCallId: call.Id, ToolName: call.Name));
          var result = await toolDispatcher.DispatchAsync(call.Name, call.ArgumentsJson, ct);
          messages.Add(new(Role.Tool, result.Output, ToolCallId: call.Id, ToolName: call.Name));
      }
      // 进入下一轮（观察 → 反思）
  }
  throw new AgentIterationLimitExceededException(agent.MaxIterations);  // 硬上限保护
  ```

- 终止判定：无 tool call = 完成；或命中 `StopCriteria`（如输出含 `FINAL_ANSWER` / 达最大迭代）。
- 安全：迭代硬上限（`agent.MaxIterations`，默认 25）；成本预算（复用 F13 租户键控 `ICostController`）；`ct` 超时。

### 3.3 ③ agent 配置字段 + 迁移（与 F31 协同）

- `Agent` 聚合新增：`AllowedToolNames`（string 列表 / json）、`MaxIterations`（int，默认 25）、`StopCriteria`（enum / string）。
- EF 迁移 `AddAgentAgenticFields`（遵循 **EF 铁律**：`dotnet ef migrations add` + `#pragma warning disable IDE0161`）。
- 种子：`AgentConfiguration` 模板补这三个字段（`DatabaseInitializer` 幂等）。
- **F31（Agent 运行时实体化）** 负责把 agent 的 `SystemPrompt`/`ModelEndpoint` 真正接通（修 `AgentCallStepExecutor.cs:50` 硬编码）——F29 的 ① 工具通道在 F31 的 agent 实体之上扩展；二者共同构成「可自主 agent」。

### 3.4 ④ agent workspace / FS 工具（真 coding 自主的硬前提）

- 新增 `WorkspaceToolExecutor`（或扩展 `IToolExecutor` 加 `Workspace` source），暴露：
  `read_file` / `write_file` / `edit_file`(diff) / `run_command` / `list_dir`。
- 全部在**现有代码沙箱 substrate（F9/F10/F11/F34）内**执行：`ProcessCodeSandbox` 真实拉起 python/node；`NetworkEnabled=false` + 语言白名单 + 超时杀 + 输出截断。
- 工具定义经 `IToolRegistry` 注册，纳入 agent 的 `AllowedToolNames` 白名单。
- 这是「Codex 式 coding 自主」与「仅文本调研 agent」的分水岭。

### 3.5 ⑤ 安全护栏（无护栏的自驱 agent 危险）

- **路径白名单**：workspace 根目录固定（每 agent / 每 run 一个沙箱目录），禁止 `../` 逃逸。
- **命令黑名单**：禁 `rm -rf /`、`format`、网络出站（沙箱已禁网）、写系统目录。
- **破坏性操作确认**：agent 发起写/删/运行 → 可经 HITL（复用 F20 `UserInput` 节点）插人工确认（可选 v1）。
- **硬成本 / 迭代上限**：`MaxIterations` + `ICostController` 预算（F13 租户键控）。
- **审计**：每次 tool call + 模型调用落 `ExecutionLog`（复用 F24 Trace）。

---

## 4. P1 项（依赖二期其他 feature）

- **⑥ 长程 durable**：agent 任务可能分钟级/数百步；当前同步执行会超时/崩丢状态。需 **F30（执行持久化）** 检查点 + 后台驱动器支撑。
- **⑦ 流式可中断 UX**：token 流式 + 中途插指令（复用 `ChatStreamAsync` + SSE 鉴权）。
- **⑧ compaction**：长程撑爆上下文 → 语义摘要（部分 = **F33（语义记忆）**）。

---

## 5. 接口草案（示意）

```csharp
// IModelClient（扩展）
Task<ModelResponse> ChatAsync(
    string modelId,
    IReadOnlyList<ChatMessage> messages,
    IReadOnlyList<ToolDefinition>? tools = null,     // 新增
    CancellationToken ct = default);

public record ModelResponse(
    string Content,
    TokenUsage? TokenUsage,
    string ModelId,
    string FinishReason,
    IReadOnlyList<ToolCall>? ToolCalls = null);      // 新增

public record ToolCall(string Id, string Name, string ArgumentsJson);
```

---

## 6. 与 Phase 7–11 / F30–F34 的衔接

- **依赖 F31（Agent 运行时实体化）**：agent 配置实体化是控制循环消费的底座；F29 的 ① 工具通道在 F31 之上扩展。
- **依赖 F30（执行持久化）**：长程 agent 需 durable 检查点（P1 项 ⑥）。
- **不替代 DAG**：确定性、可审计流程仍用显式图；自主 agent 最自然形态是**作为 DAG 里的一个 node**（混合编排）——把 `AgenticOrchestrator` 包成 `StepType.Agentic` 节点，由 `SequentialOrchestrator` 调度。

---

## 7. 验收子项（v1 最小闭环）

> 2026-08-21 落地状态：①–⑤ 全部 ✅（详见 §12 质量门清单与报告 `docs/quality/f29-agentic-gate.md`）。

- **① ✅** 模型客户端能吐出 `ToolCalls`（`SemanticKernelModelClientToolCallTests`：mock `IChatCompletionService.GetChatMessageContentsAsync` 验证 declare-only 接线 + `FunctionCallContent` 解析；SK 1.30 实为 `FunctionCallContent`/`FunctionResultContent`，位于 `Microsoft.SemanticKernel` 命名空间，构造函数参数序为 `(functionName, pluginName, id, arguments)` / `(functionName, pluginName, callId, result)`）。
- **② ✅** 控制循环 standalone 跑通：`AgenticOrchestratorTests`（Stub 模型 + 内存工具注册表 + 假执行器）验证 tool call → 执行 → 无 tool call 终态 → 结构化结果；迭代上限抛 `AgentIterationLimitExceededException`；白名单拦截 `tool_not_allowed`。
- **③ ✅** 三个 agent 字段落库 + EF 迁移 `AddAgentAgenticFields`（AllowedToolNamesJson/MaxIterations/StopCriteria）+ `DatabaseInitializer` 幂等种子（F29 demo agent，固定 Guid `3333…3301`，工作区工具白名单）。
- **④ ✅** Workspace 工具在真实 `ProcessCodeSandbox` 内读/写/编辑/列出/跑命令（`WorkspaceToolExecutorTests`：写→读回环、edit 替换、list 相对路径、run_command 工作目录断言 `ap_workspace_`）。
- **⑤ ✅** 路径逃逸拒绝（`escapes`）、命令黑名单（`rm -rf /` → `guardrail`）、迭代上限单测。
- **⑥（P1）** 长程 durable 接 F30（未开工）。
- **⑦（P1）** 流式可中断 UX（未开工，UI 已留运行弹窗展示最终回答）。

---

## 8. 诚实风险（非工程能解）

1. **模型质量**：循环能否跑通取决于 `DefaultModelId` 的 function-calling / 自规划能力；OpenAI-compat / DeepSeek / vLLM 差异大，须选够强的模型。
2. **跑飞 / 成本**：agent 可能死循环调工具 → 必须有硬迭代上限 + 成本预算。
3. **「跑完 ≠ 正确」**：最终 diff / 产出仍需人审 + F24 eval；Codex 自己也要人 review。
4. **DAG 不被替代**：自主 agent 是 DAG 的一个 node，混合模式优先。

---

## 9. 质量门怎么过（feature-builder）

- 三道门：`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`。
- **高风险闸口**：接口契约（`IModelClient` 扩展）/ 鉴权（工具调用须走租户上下文）/ 路由（若暴露新端点）——按项目约定**先设计后实现、停下问人**。
- `.quality-gate.json` 推进 `f29-agentic-agent-primitive`，含 `cleared:true` + `codebaseOptimizer`。
- **测试工程须纳入 `AgentPlatform.sln`**（铁律）。

---

## 10. 最小验证路径

1. **先做「research agent」standalone**：目标 → 调 2–3 个工具（如 `web_search` / `read_file`）→ 跑通循环 → 产出结果。不依赖前端。
2. standalone 跑通后，把 `AgenticOrchestrator` 包成 `StepType.Agentic` 节点，经 `SequentialOrchestrator` 调度，作为 DAG 的一个 node 暴露。
3. 再补前端：Agent 配置页加「允许工具 / 最大迭代」表单 + 运行页展示思考/工具调用流。

---

## 11. 实现细节（接地气的接口 / 接线 · 2026-08-12 补充）

> 本节把 §3 / §4 的改造点落到**真实代码形状 + 接线步骤**，所有引用均为 2026-08-12 直接核实。结论先行：**地基已埋 ~70%**，尤其 `ResearchCommandHandler`（F5 研究 agent）已是完整「规划→行动→观察→综合」自驱循环，可直接抄结构作 `AgenticOrchestrator` 模板。

### 11.0 复用资产清单（实现前必读）

| 现有模块 | 文件（核实位置） | 在本方案的角色 |
|---|---|---|
| `ToolCallingDispatcher.DispatchAsync(toolName, parametersJson, ct)` | `Application/Tools/ToolCallingDispatcher.cs:43` | **工具执行半边已齐**：注册表查找 + Source 路由 + 禁用校验，返回 `ToolExecutionResult` |
| `IToolExecutor` × 3（Native / Skill / MCP） | `Application/Abstractions/IToolExecutor.cs:24` | 工具执行器接口，按 `ToolSource` 分发 |
| `IToolRegistry.GetByNameAsync / GetAllAsync` + `ToolDefinition.ParametersSchema` | `Application/Abstractions/IToolRegistry.cs` / `ToolDefinition.cs` | 工具 schema 来源（JSON Schema 字符串） |
| `ICodeSandbox.RunCodeAsync(code, language, timeout, ct)` | `CodeStepExecutor.cs:50` 已用 | **FS / 跑命令的物理底座**，Docker 强隔离复用 |
| `IModelClient.ChatStreamAsync` + `ToChatHistory` 已含 Tool 角色 | `SemanticKernelModelClient.cs:220,148` | 流式 + tool-role 消息映射底座 |
| `MaxSummaryTokens`(=8000) + `ITokenCounter` | `StateMachineSettings.cs:50` / `SequentialOrchestrator.cs:482` | 截断现状，compaction 要替换它 |
| `ResearchCommandHandler`（F5） | `Application/Research/` | **自驱循环参考实现**，直接抄结构 |

---

### 11.1 ① 工具成为 agent 可调用的一等公民

**现状缺口**：`ToolCallingDispatcher` 能执行工具，但**没有任何地方去问模型「该调哪个工具」**——`IModelClient.ChatAsync` 现只返回纯文本（`ModelResponse.Content`），`SemanticKernelModelClient.ToChatHistory` 还把 `ToolCallId / ToolName` 直接丢弃（`:142`）。

**实现三步：**

**Step 1 — 扩 `IModelClient`（`IModelClient.cs:18,51`）**

```csharp
public record ToolCall(string Id, string Name, string ArgumentsJson);

// ChatAsync 增加可选 tools 参数
Task<ModelResponse> ChatAsync(
    string modelId,
    IReadOnlyList<ChatMessage> messages,
    IReadOnlyList<ToolDefinition>? tools = null,   // 新增
    CancellationToken ct = default);

// ModelResponse 增加 ToolCalls
public record ModelResponse(
    string Content,
    TokenUsage? TokenUsage,
    string ModelId,
    string FinishReason,
    IReadOnlyList<ToolCall>? ToolCalls = null);    // 新增
```

**Step 2 — `SemanticKernelModelClient` 接 SK function calling（declare-only，不自调）**

> ⚠️ **接线纠偏（重要）**：设计文档 §3.1 写的 `ToolCallBehavior = AutoInvokeKernelFunctions = false` 是**错误写法**——`ToolCallBehavior.AutoInvokeKernelFunctions` 本身已是「自动调用」实例，赋 `false` 会得到该实例，反而触发 SK 自调。正确做法：注册 function 到 kernel 后，使用 **`ToolCallBehavior.NoKernelFunctions`**（或默认）让 SK 仅「请求」工具调用并返回 `ToolCallContent`，由本平台循环接管调用（这正是 agentic 的本质——执行权留在平台）。

```csharp
// ChatAsync 内构造 settings
var settings = new OpenAIChatPromptExecutionSettings
{
    ToolCallBehavior = ToolCallBehavior.NoKernelFunctions,   // 仅声明，不自调
    Tools = tools?.Select(ToOpenAIFunction).ToList()
};
var reply = await service.GetChatMessageContentAsync(chatHistory, settings, kernel, ct);

var calls = reply.Items.OfType<ToolCallContent>()
    .Select(c => new ToolCall(c.Id, c.Name, c.Arguments?.ToString() ?? "{}"))
    .ToList();
return new ModelResponse(reply.Content ?? "", usage, modelId, "tool_calls", calls);
```

`ToOpenAIFunction`：把 `ToolDefinition.ParametersSchema`（JSON Schema 字符串）映射成 SK `OpenAIFunction`（用 `JsonSchemaExporter` / 手写属性 → `OpenAIParameterMetadata`）。

**同时必须修 `ToChatHistory`（:137）**：当前它把 assistant 工具调用消息丢成纯文本、把工具结果丢成纯文本。补映射：
- assistant 工具调用 → `ChatMessageContent(items: [ToolCallContent(Id, Name, Args)])`
- 工具结果 → `ToolMessageContent(toolCallId, resultText)`

这两处映射补上后，多轮工具对话的上下文才会被正确回灌。

**Step 3 — `AgenticOrchestrator` 循环（参考 `ResearchCommandHandler` 结构，复用现有 dispatcher）**

```csharp
for (var i = 1; i <= agent.MaxIterations; i++)
{
    resp = await modelClient.ChatAsync(modelId, messages, allowedToolDefs, ct);
    if (resp.ToolCalls is null or { Count: 0 }) break;      // 模型认为完成

    foreach (var call in resp.ToolCalls)
    {
        messages.Add(new(Role.Assistant, "", ToolCallId: call.Id, ToolName: call.Name));
        var result = await toolDispatcher.DispatchAsync(call.Name, call.ArgumentsJson, ct);  // ← 零改动复用
        messages.Add(new(Role.Tool, result.Output, ToolCallId: call.Id, ToolName: call.Name));
    }
}
```

这就是「函数调用协议 + 结果回灌上下文」的全部——**`ToolCallingDispatcher` 和三个 `IToolExecutor` 零改动复用**，只需新增 dispatcher 之上的循环。

---

### 11.2 ② 文件系统 / 代码库操作能力（让 agent 真干活）

**现状**：`ICodeSandbox.RunCodeAsync` 已被 `CodeStepExecutor` 用于 DAG 代码节点，且已有 Docker 强隔离（F9/F10/F11/F34）。**agent 的文件 / 命令工具 = 同一个沙箱**，安全隔离直接继承。

**实现**：新增 `WorkspaceToolExecutor : IToolExecutor`（`Source = ToolSource.Workspace`，需给 `ToolSource` 枚举加一个值），注入现有 `ICodeSandbox`：

```csharp
public ToolSource Source => ToolSource.Workspace;
// 注册为 ToolDefinition（Source = Workspace），纳入 agent AllowedToolNames 白名单：
//   read_file(path)         → 读 workspace 内文件（截断到 N 字节）
//   write_file(path, text)  → 在 workspace 根内写
//   edit_file(path, old, new) → 字符串替换（diff 式）
//   run_command(cmd)        → _sandbox.RunCodeAsync(cmd, "shell", timeout)  // 跑测试 / 看 diff 走这
//   list_files(pattern)     → 列 workspace 内文件
//   git_diff()              → 在 workspace 内跑 `git diff`
```

**安全护栏（必须）**：workspace = **每次 run 新建的临时目录**（非宿主任意路径）；`read / write` 一律相对 workspace 根，拒绝 `..` 穿越、拒绝 workspace 外绝对路径。`run_command` 复用沙箱资源限额 + 超时 kill（已有）。

> ⚠️ **接口注意**：`ICodeSandbox` 抽象目前只暴露 `RunCodeAsync`。要让 agent「跑命令 / 跑测试」还需补一个 `RunCommandAsync`（`ProcessCodeSandbox` 内部已有同名方法供隔离层用，提到接口即可）。

**注册**：agent 启动时把上述 `ToolDefinition` 动态 `Register` 进 `IToolRegistry`（或种子），纳入 agent `AllowedToolNames` 白名单——11.1 的循环即可动态选中它们。

---

### 11.3 ③ 流式 + 可中断 UX

**现状**：`ChatStreamAsync` 已逐 token 产出（`SemanticKernelModelClient.cs:220`）；前端 SSE 鉴权链路已存在。

**实现**：
- **后端**：`AgenticOrchestrator` 暴露流式变体 `IAsyncEnumerable<AgentEvent>`，`AgentEvent` 联合类型 =
  `Token(string)`（思考 token，来自 `ChatStreamAsync`）/ `ToolCall(ToolCall)` / `ToolResult(string)`（每轮迭代 emit）/ `Done(...)`。
  新增 `POST /api/v1/agents/{id}/runs/stream` 返回 `text/event-stream`，两类事件：`token` 和 `tool_call / tool_result`。
- **可中断**：客户端发 `POST /runs/{runId}/interrupt` 带用户指令 → 服务端设标志（复用现有 `ExecutionLog` 状态机 + `CancellationTokenSource`）。循环**每轮迭代开头检查**：若有待处理插话，注入为 `ChatMessage(Role.User)` 后继续；或 halt。本质与 Phase 5 的 HITL `Paused / Resume` 同构，可复用那套持久化。
- **前端**：运行视图订阅 SSE，实时渲染 token 流 + 工具调用时间线 + 一个「中途指令」输入框。

> 设计约定：思考 token 走 `ChatStreamAsync` 流式；工具调用走非流 `ChatAsync`（工具调用不需逐字流，整块 dispatch 即可）。

---

### 11.4 ④ 自动 compaction / 长上下文管理

**现状**：`MaxSummaryTokens`(8000) 在 `SequentialOrchestrator:482` / `NegotiationOrchestrator:303` 做**明文 FIFO 截断**（超预算丢最旧片段），无 embedding、无 LLM 摘要。

**实现**：新增 `ICompactionService.CompactAsync(messages, budget, ct)`：

```csharp
if (tokenCounter.Count(messages) <= budget) return messages;   // 复用现有 ITokenCounter

var keep    = new[] { messages.System, messages.FirstUserGoal };   // 系统提示 + 首条目标
var oldest  = messages.OldestChunk();                              // 最旧 N 条
var summary = await modelClient.ChatAsync(modelId, Summarize(oldest));  // 廉价一次摘要
var recent  = messages.NewestChunk();                             // 最近 K 条原样保留

return keep
    .Concat(new[] { new ChatMessage(Role.System, "[压缩历史] " + summary) })
    .Concat(recent);
```

**接线点两处**：
- `AgenticOrchestrator` 循环每轮查预算（长程 agent 必用）——这是内联版 compaction。
- 可选回灌 `SequentialOrchestrator` / `NegotiationOrchestrator` 替换现有 FIFO 截断（向后兼容）。

> **与 F33 的递进关系**：F29 的 compaction 是「内联 LLM 摘要」版（替换 FIFO 截断，无 embedding）；**F33（语义记忆）** 在此基础上再加 embedding / episodic 写回 / 跨会话沉淀——二者递进，F29 不阻塞 F33。

---

### 11.5 落地顺序（与 §3–§4 一致）

1. **11.1 ① 模型工具通道**（最大 blocker，SK 原生支持，纯接线）
2. **11.2 ② WorkspaceToolExecutor**（复用沙箱，物理能力就位）
3. ① + ② 接成最小 `AgenticOrchestrator` 循环 → 先跑通 standalone research agent
4. **11.4 ④ compaction** 接进循环（长程必需）
5. **11.3 ③ 流式 / 可中断** 最后做 UX（依赖前面循环稳定）

四者**全部落在 F29（二期第一个 feature）范围内**，不另开 feature；④ 的部分能力会被 F33 正式化。

---

## 12. F29 Phase 5 质量门清单（ddd-phase-quality-gate，2026-08-21）

Gate Status: **PASS** — P0=0 / P1=0 / P2=0 / P3=0（无 waiver）。

### 模式 1：Checklist（8 类）

| # | 类别 | 结论 |
|---|------|------|
| 1 | Pre-flight 版本审计 | ✅ SK 1.30.0 实 API 经反射核实（`IChatCompletionService` 仅 `GetChatMessageContentsAsync` 复数；`FunctionCallContent`/`FunctionResultContent` 在 `Microsoft.SemanticKernel` 命名空间；`OpenAIFunction` ctor internal，经 `KernelFunctionMetadata.ToOpenAIFunction()` 构建；`ToolCallBehavior.EnableFunctions(fn, autoInvoke:false)` declare-only）。此前误用的 `ToolCallContent`/`NoKernelFunctions`/`OpenAIChatPromptExecutionSettings`/3 参 `OpenAIFunction` 均不存在，已全部改正 |
| 2 | BDD/测试先行 | ✅ 验收 ①–⑤ 对应 3 个新测试类 13 用例 + E2E `agentic-run.feature`（先写 feature 再实现 UI 步骤） |
| 3 | DDD 分层规则 | ✅ 接口在 Application（`IModelClient`/`IToolRegistry`/`ICodeSandbox`/`IStepExecutor`/`IToolExecutor`），实现 internal sealed 在 Infrastructure（`SemanticKernelModelClient`/`InMemoryToolRegistry`/`ProcessCodeSandbox`/`WorkspaceToolExecutor`/`AgenticStepExecutor`），控制循环 `AgenticOrchestrator` 在 Application.Agents.Agentic（纯编排，无 IO 依赖） |
| 4 | DI 注册完备 | ✅ `ToolCallingDispatcher`(413)/`AgenticOrchestrator`(415)/`WorkspaceToolExecutor`(420)/`AgenticStepExecutor`(349) 均已注册；`IToolRegistry` 单例工厂内种子 6 个工作区工具；`ModelDefaults` 由 Api `Configure<ModelDefaults>` 提供（种子可解析） |
| 5 | Configuration-First | ✅ 沙箱/模型/限流均走 IOptions；Agent 字段走聚合字段（非配置） |
| 6 | EF Core 映射同步 | ✅ `AgentConfiguration` 补 3 字段映射；迁移 `20260821043044_AddAgentAgenticFields`（含 IDE0161 pragma）；`MigrateAsync` 为 schema 唯一来源 |
| 7 | 并发与生命周期 | ✅ `IToolRegistry` 单例用 `ConcurrentDictionary`；`WorkspaceToolExecutor` 每请求 scoped + `Dispose` 清理临时根目录；`InMemoryToolRegistry.Register` `TryAdd` 防重；迭代上限硬保护防跑飞 |
| 8 | 横切基础设施 | ✅ 无新增 CORS/异常面；RunAgent 未找到 agent 已改 404（catch `InvalidOperationException`） |

### 模式 2：Audit（12 类）

| 类别 | 结果 |
|------|------|
| DI 缺口 | 0（见上） |
| 分层违规 | 0 |
| EF 映射缺口 | 0 |
| 硬编码 | 0（种子固定 Guid 为幂等设计，已注释） |
| 缺 CancellationToken | 0（所有 async 均带 ct） |
| 缺 internal sealed | 0 |
| 并发风险 | 0 |
| 缺空值守卫 | 0（`ArgumentNullException.ThrowIfNull`/`ThrowIfNullOrWhiteSpace`） |
| API 基础设施 | 0 |
| 蓝图漂移 | 0 |
| 缺 XML 文档 | 0（TreatWarningsAsErrors 强制） |
| 死代码/空洞类 | 0（新类型全部有引用点：DI/控制器/测试） |

### Review-Fix 记录（ddd-code-reviewer 对抗审查 + 本门审计修复项）

| 严重级 | 文件 | 发现 | 修复 |
|--------|------|------|------|
| P0 | `SemanticKernelModelClient.cs` | `FunctionCallContent`/`FunctionResultContent` 构造参数序错位（`(functionName, pluginName, id, arguments)` / `(functionName, pluginName, callId, result)`），助手 tool_calls 回显与 tool 结果字段全部错位 | 改为命名参数 `functionName:`/`id:`/`arguments:`/`callId:`/`result:`；测试 `ChatAsync_ToolCallHistory_RoundTripsToChatHistory` 抓出 |
| P0 | `WorkspaceToolExecutor.cs` | `Encoding.UTF8` 写文件带 BOM，读回内容前置 `\uFEFF` | 引入 `Utf8NoBom = new UTF8Encoding(false)`，write/edit 统一使用 |
| P1 | `SemanticKernelModelClient.cs` | `ToolCallBehavior.NoKernelFunctions` 不存在（SK 1.30 为 `EnableKernelFunctions`/`EnableFunctions`）；`OpenAIChatPromptExecutionSettings` 应为 `OpenAIPromptExecutionSettings`；`OpenAIFunction` 3 参 ctor 不存在 | 全部改用真实 API；`ToOpenAIFunction` 经 `KernelFunctionMetadata`+参数 schema 解析 + `ToOpenAIFunction()` 扩展构建 |
| P1 | `WorkspaceToolExecutor.cs` | run_command/git_diff 未指定工作目录，命令跑在宿主 CWD | `ICodeSandbox.RunCommandAsync` 增 `workingDirectory` 参数，传 `EnsureRoot()` |
| P2 | `AgentsController.cs` | RunAgent 未找到 agent → 500 | catch `InvalidOperationException` → 404 |
| P2 | `CreateAgentCommand`/`AgentsController` | 位置参数错位（新字段插入后 `ConfigurationId` 前移） | 控制器改命名参数 |
| P2 | `SemanticKernelModelClient.cs` | `c.Id` 可空 → CS8604 | `c.Id ?? string.Empty` |
| P3 | `AgenticStepExecutor.cs` | 类内 `string StepType` 属性遮蔽枚举类型，`StepType.Agentic` 无法解析 | 全限定 `AgentPlatform.Domain.Enums.StepType.Agentic` |

### 验证

- `dotnet build src/AgentPlatform.sln`：0 warning / 0 error。
- `dotnet test`：Application.Tests 192 / Infrastructure.Tests 147+6skip / Api.Tests 35 / ArchitectureTests 9 / SpecFlowTests(BDD) 115 全绿。
- 前端：`tsc --noEmit` 0；eslint 改动文件 0 error；`bddgen` + Playwright：`agentic-run.feature` 通过；全量 `@e2e` 26/26 通过。
