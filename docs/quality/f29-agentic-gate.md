# F29 · Agentic Agent Primitive — 质量门报告

- **Feature**: F29 · Agentic Agent Primitive（自主 Agent 控制循环原语，P0 置顶，独立轨道）
- **分支**: `feat/f29-agentic-agent-primitive`
- **日期**: 2026-08-21
- **Gate Status**: **PASS** — P0=0 / P1=0 / P2=0 / P3=0（无 waiver）

## 1. ddd-code-reviewer（对抗式审查）

对 F29 增量做了对抗式审查（含状态机/编排器 Section A + 资源生命周期 Section H2 覆盖），发现并**全部即时修复**：

| 严重级 | 文件 | 发现 | 修复 |
|--------|------|------|------|
| P0 | `SemanticKernelModelClient.cs` | SK 1.30 `FunctionCallContent`/`FunctionResultContent` 构造参数序为 `(functionName, pluginName, id, arguments)` / `(functionName, pluginName, callId, result)`，此前位置实参导致助手 tool_calls 回显与 tool 结果字段错位 | 改为命名参数；`ChatAsync_ToolCallHistory_RoundTripsToChatHistory` 测试锁定 |
| P0 | `WorkspaceToolExecutor.cs` | `Encoding.UTF8` 写文件带 BOM，读回内容前置 `\uFEFF` | `Utf8NoBom = new UTF8Encoding(false)` |
| P1 | `SemanticKernelModelClient.cs` | `ToolCallBehavior.NoKernelFunctions`/`OpenAIChatPromptExecutionSettings`/3 参 `OpenAIFunction` 在 SK 1.30 不存在；`OpenAIFunction` ctor internal | 反射核实后改用 `ToolCallBehavior.EnableFunctions(fn, autoInvoke:false)` + `OpenAIPromptExecutionSettings` + `KernelFunctionMetadata.ToOpenAIFunction()` |
| P1 | `WorkspaceToolExecutor.cs` | run_command/git_diff 未指定工作目录，命令跑在宿主 CWD | `ICodeSandbox.RunCommandAsync` 增 `workingDirectory`，传 `EnsureRoot()` |
| P2 | `AgentsController.cs` | RunAgent 未找到 agent → 500 | catch `InvalidOperationException` → 404 |
| P2 | `AgentsController.cs` / `CreateAgentCommand` | 新字段插入后位置参数错位 | 控制器改命名参数 |
| P3 | `AgenticStepExecutor.cs` | 类内 `string StepType` 属性遮蔽枚举类型 | 全限定 `AgentPlatform.Domain.Enums.StepType.Agentic` |

控制流追踪：`POST /agents/{id}/runs` → `RunAgentGoalCommandHandler` → `AgenticOrchestrator.RunGoalAsync`（循环 ChatAsync→DispatchAsync→回灌→判停/上限）→ `AgenticStepExecutor`（DAG 节点）——所有接口均有 DI 注册（`ToolCallingDispatcher`/`AgenticOrchestrator`/`WorkspaceToolExecutor`/`AgenticStepExecutor`），所有 async 均 await。

## 2. ddd-phase-quality-gate（阶段结构门）

- **Checklist（8 类）已嵌入** `features/agentic-agent-primitive.md` §12，全 ✅。
- **Audit（12 类）**：DI 缺口 0 / 分层违规 0 / EF 映射缺口 0 / 硬编码 0 / 缺 CancellationToken 0 / 缺 internal sealed 0 / 并发风险 0 / 缺空值守卫 0 / API 基础设施 0 / 蓝图漂移 0 / 缺 XML 文档 0 / 死代码·空洞类 0。
- Gate Status: **PASS**（P0–P3 = 0 open）。

## 3. codebase-optimizer（多轮优化）

- **模式**：自动化模式，但**不建优化分支/不单独 commit/push**——feature-builder 硬约束要求所有改动落在 `feat/f29-agentic-agent-primitive` 单一提交（与 F12 等既往 feature 一致）。
- **聚焦范围**：F29 增量（`AgenticOrchestrator`/`SemanticKernelModelClient`/`WorkspaceToolExecutor`/`AgenticStepExecutor`/RunAgent 命令与 API/种子/3 测试类 + 前端 AgentsPage）。
- **七维度扫描结论**：架构 0 / 代码质量 0 / 正确性 0 / 测试 0 / 性能 0（工作区工具 200KB 截断、迭代上限防跑飞）/ 安全 0（路径逃逸 + 命令黑名单 + 白名单护栏）/ 工程化 0（CI 约定一致）。此前所有 stub→生产就绪修复已在 ddd-code-reviewer 记录中吸收；**无遗留 open 项** → Round 1 即收尾。

## 4. 验证

- `dotnet build src/AgentPlatform.sln`：**0 warning / 0 error**（13 项目全编译）。
- `dotnet test`：Application.Tests **192** / Infrastructure.Tests **147+6skip** / Api.Tests **35** / ArchitectureTests **9** / SpecFlowTests（BDD）**115**，全绿 0 失败。
- 前端：`tsc --noEmit` **0**；eslint（改动文件）**0 error**（3 个 pre-existing warning 未新增）；`bddgen` + Playwright `agentic-run.feature` **通过**；全量 `@e2e` **26/26 通过**。

## 5. 诚实风险（承接设计 §8）

1. **模型质量**：真实 function-calling 依赖所选模型能力（OpenAI-compat / DeepSeek / vLLM 差异大）；本 feature 用 stub/单测锁定循环机制，真实模型联调需配 Key 后人工验证。
2. **跑飞/成本**：已设硬迭代上限（默认 25）+ 白名单护栏；成本预算复用 F13 `ICostController` 为后续项。
3. **「跑完 ≠ 正确」**：最终产出仍需人审 + F24 eval（与设计一致，非本 feature 解）。
