# F5 · 行动层落地（Agent 真正能做事）— 质量门禁报告

> 分支：`feat/f5-action-layer`　|　feature-builder 全栈流水线　|　日期：2026-07-24
> 报告引用：`.quality-gate.json` → `docs/quality/f5-action-layer-gate.md`
> 设计文档：`features/action-layer.md`　|　🔴 高风险（接口/契约/路由级）— 已按 feature-builder 护栏先出设计文档 + 选项，经用户确认范围（A1+A2+A3 一并纳入）后实施。

## 概述

F5 是 Phase 6「行动层」首个 epic，目标是把原先**空心**的执行层变成**真实副作用**：

- **A1 原生工具真实 HTTP**：`NativeToolExecutor` 从「返回假成功」改为对 `ToolDefinition.EndpointUrl` 发起真实 HTTP 调用，回传真实响应体/状态码；非 2xx / 超时 / 连接失败 → 精准回打真实错误（符合 Phase 6 critic 范式）。
- **A2 代码沙箱真实进程**：新增 `ProcessCodeSandbox`（用 `System.Diagnostics.Process` 拉起 python / node 真实运行代码），替代原本伪造成功的 `DockerCodeSandbox`；捕获真实 stdout / stderr / ExitCode / 超时杀进程。Docker 在本沙箱不可用，用户确认采用进程沙箱为默认真实路径。
- **A3 Tool / Code 工作流节点**：新增 `ToolStepExecutor` / `CodeStepExecutor`，注册为 `StepType.Tool=6` / `StepType.Code=7` 节点执行器，经既有 `ResolveExecutor`（`HandlesType` 匹配）真实路由。前端 DAG 画布补 Tool / Code 节点（调色板 / 图标 / 配置面板 / node-type 映射）。

## 三道质量门禁结论

| 门禁 | 结论 | 摘要 |
| --- | --- | --- |
| ddd-code-reviewer | **PASSED** | 对抗式审查 F5 后端 + 前端改动。详查最易错点：DI 生命周期（dispatcher/executor 全 scoped，无 captive dependency）、`ToolStepExecutor` 参数透传（`GetRawText` 保留原始 JSON）、`ProcessCodeSandbox` 超时杀进程与 `using` 释放、前端 `StepType` int 与后端枚举一致性。均无缺陷，0 open。新增 13 例真实副作用单测全绿 |
| ddd-phase-quality-gate | **PASS** | P0=0 P1=0 P2=0 P3=0。12 类审计：DI 注册完整（新增 `ICodeSandbox`/`IToolExecutor` 实现均注册，无空洞接口）、DDD 分层正确（实现全在 Infrastructure，配置 POCO 在 Application.Abstractions）、EF 映射无需变更（`StepType` 为 int 枚举，无 schema 改变）、无硬编码密钥、前端 `internal sealed`/zustand 不可变/空值守卫/strict 全扫 0 open。修复 1 处 P1 空心类（`DockerCodeSandbox` 伪造成功 → 改为显式抛异常，消除静默假成功）。checklist 已嵌入 `features/action-layer.md` §6 |
| codebase-optimizer | **PASSED** | Round F5-01，0 open。七维度扫描 F5 改动：架构 执行层按 Tools/Sandbox/Workflows 分目录、代码质量 0 `any`+`strict`+`internal sealed` 带 XML 文档、正确性 真实 HTTP+真实子进程由单测覆盖、测试 新增 13 例、性能 `HttpClient` 走 `IHttpClientFactory` 池化 + 临时文件 `finally` 清理 + `Process` `using`、安全 `NetworkEnabled=false` 默认 + 语言白名单 + 超时杀进程 + 输出截断、工程化 `dotnet build` 0 警告 0 错误 / `tsc` 0 错误 / 无死代码（Docker 空心类已消除）。注：本门禁按 feature-builder 约束在 `feat/f5-action-layer` 分支分析+修复，**未**新建 `codebase-optimizer/{date}` 分支或推送 |

## 真实副作用验收（A1 / A2 / A3 已核对）

> 本门禁强制要求核对 A1/A2/A3 的**真实副作用**，而非桩/假成功。以下均经 `dotnet test` 真实执行路径验证（非 mock 执行器逻辑）：

### A1 · NativeToolExecutor 真实 HTTP（`Tools/NativeToolExecutorTests.cs`，5 例）

用真实 `HttpMessageHandler`（实际走 `client.SendAsync` 路径，仅 transport 由测试桩充当「服务器」）验证：

- `POST` 默认 + 有参 → 请求体为 JSON，`Success=true` 且 `Output` 含响应体。
- 空参数 → 走 `GET` 且**无**请求体。
- `parameters` 含 `"httpMethod":"GET"` → 即便有参也走 `GET`。
- 非 2xx（500）→ `Success=false` 且 `ErrorMessage` 含 `500`（精准回打真实状态）。
- `EndpointUrl` 缺失 → 失败且 `ErrorMessage` 含 `EndpointUrl`。

### A2 · ProcessCodeSandbox 真实进程（`Sandbox/ProcessCodeSandboxTests.cs`，5 例）

本沙箱 `python`/`python3`/`node` 均在 PATH，真实拉起解释器子进程：

- `print('hello from sandbox')` → `Success=true`、`ExitCode=0`、stdout 含该字符串（**真实 stdout 捕获**）。
- `console.log('js hello')` → `Success=true`、stdout 含该字符串。
- `raise ValueError('boom')` → `Success=false`、非 0 退出、`stderr` 含 `boom`（**真实 stderr 捕获**）。
- `ruby` 不在白名单 → `Success=false`（语言门禁生效）。
- `import time; time.sleep(30)` + `timeout=2` → 进程被**真实杀死**、`Success=false`、耗时 `<15s`（未挂起）、`Stderr` 含「超时」（超时杀进程生效）。

### A3 · Tool / Code 节点执行器（`Workflows/ToolStepExecutorTests.cs` + `CodeStepExecutorTests.cs`，6 例）

- `ToolStepExecutor` 经**真实** `ToolCallingDispatcher`（真实 `IToolRegistry` + 真实执行器解析）派发 → `executor.Received(1)`、`Success` 映射 `Output`；工具失败 → `FailedRetry`；缺 `toolName` → `FailedRollback`。
- `CodeStepExecutor` 调真实 `ICodeSandbox` → `RunCodeAsync` `Received(1)`、`Success` 映射 `Output`；沙箱失败 → `FailedRetry`；缺 `code` → `FailedRollback`。

### 运行时路由闭环

`WorkflowNode` 实现 `IWorkflowExecutable.Type => Type`（其 `StepType`）；三个 `ResolveExecutor`（`WorkflowNodeRunner` / `SequentialOrchestrator` / `NegotiationOrchestrator`）按 `e.HandlesType == step.Type.Value` 匹配。新增 `ToolStepExecutor(HandlesType=Tool)` / `CodeStepExecutor(HandlesType=Code)` 已注册为 scoped，故存为 `StepType.Tool/Code` 的节点在运行时**确定性路由**到新执行器。全仓无对 `StepType` 的穷举 `switch` 会拒绝新枚举值。

## 模型一致性校验（Phase 3）

- 后端：`dotnet build src/AgentPlatform.sln` **0 警告 0 错误**；`StepType` 枚举新增 `Tool=6`/`Code=7`，无 EF 迁移需求。
- 前端：`tsc --noEmit` **0 error**。`types/index.ts` 的 `StepType` 常量对象与后端枚举值**完全一致**（Start=0…Knowledge=5, Tool=6, Code=7）；`STEP_TYPE_TO_NODE_TYPE` / `NODE_TYPE_TO_STEP_TYPE` / `STEP_TYPE_LABEL` / `defaultConfig` 均补 Tool/Code；`NodePalette`/`DagNode`/`NodeConfigPanel`/`WorkflowCanvasPage` 补节点类型与配置面板。
- 全链路测试：`dotnet test src/AgentPlatform.sln` **230 例全绿**（SpecFlow 41 / Arch 6 / App 82 / Infra 80 / Integration 5 / Api 16），含 F5 新增 13 例真实副作用单测。

## 改动文件清单

后端（新增 / 修改）：

- `src/AgentPlatform.Domain/Enums/StepType.cs` — 新增 `Tool=6` / `Code=7`（保留 P2 预留位注释）。
- `src/AgentPlatform.Application/Abstractions/SandboxSettings.cs`（新增）— 沙箱/HTTP 安全边界配置 POCO。
- `src/AgentPlatform.Infrastructure/Tools/NativeToolExecutor.cs` — 重写为真实 HTTP（方法解析 + 真实成功/失败/超时回打）。
- `src/AgentPlatform.Infrastructure/Tools/SkillPackageExecutor.cs`、`McpClient.cs` — 保留 Phase 6 占位（补 `TODO(Phase6)` 标记，A1 仅要求 NativeToolExecutor 真实化）。
- `src/AgentPlatform.Infrastructure/Sandbox/ProcessCodeSandbox.cs`（新增）— 真实进程沙箱（stdout/stderr/ExitCode/超时杀）。
- `src/AgentPlatform.Infrastructure/Sandbox/DockerCodeSandbox.cs` — 由伪造成功改为显式抛异常（消除 P1 空心类）。
- `src/AgentPlatform.Infrastructure/Workflows/ToolStepExecutor.cs`（新增）— `StepType.Tool` 执行器，真实派发。
- `src/AgentPlatform.Infrastructure/Workflows/CodeStepExecutor.cs`（新增）— `StepType.Code` 执行器，真实沙箱调用。
- `src/AgentPlatform.Infrastructure/DependencyInjection.cs` — `Configure<SandboxSettings>` + `AddHttpClient()` + 条件注册 `ICodeSandbox`（Docker/Process）+ 注册 `ToolStepExecutor`/`CodeStepExecutor`。
- `src/AgentPlatform.Api/appsettings.json` — 新增 `Sandbox` 配置节（Provider=Process 等）。
- `src/AgentPlatform.Infrastructure.Tests/{Tools,Sandbox,Workflows}/*Tests.cs`（新增 4 文件，13 例）。

前端（新增 / 修改）：

- `src/AgentPlatform.Web/src/types/index.ts` — `StepType` 补 `Tool=6`/`Code=7`；`NodeConfig` 补 `toolName`/`parameters`/`code`/`language`。
- `src/AgentPlatform.Web/src/components/canvas/NodePalette.tsx`、`DagNode.tsx` — 补 Tool/Code 图标与调色板项。
- `src/AgentPlatform.Web/src/stores/workflowCanvasStore.ts` — 补 Tool/Code 三类映射与 `defaultConfig`。
- `src/AgentPlatform.Web/src/components/canvas/NodeConfigPanel.tsx` — 补 Tool（toolName + parameters）/ Code（language + code）配置面板。
- `src/AgentPlatform.Web/src/pages/WorkflowCanvasPage.tsx` — `nodeTypes` 补 `tool`/`code`。

## 已知残留（非阻断，已记录 waiver）

- **Docker 真实隔离 → Phase 6**：`DockerCodeSandbox` 已改为显式抛异常，避免静默假成功；真实容器执行需接入 Docker.DotNet + 守护进程，列入 Phase 6。
- **Skill / MCP 执行器占位 → Phase 6**：`SkillPackageExecutor` / `McpClient` 仍返回占位结果，设计文档 `features/action-layer.md` 明确 A1 仅要求 `NativeToolExecutor` 真实化，二者为 Phase 6 范围，带 `TODO(Phase6)` 标记。记为 waiver（target Phase 6）。
- **进程模式网络隔离**：`ProcessCodeSandbox` 无法在 OS 层强制禁网，已以 `NetworkEnabled=false`（默认）+ `AGENT_PLATFORM_SANDBOX_OFFLINE` 环境标记 + 语言白名单 + 超时杀进程 + 输出截断作为缓解；更强隔离待 Docker 模式。
- **全链路 e2e**：含 Tool/Code 节点的端到端工作流运行需后端 + Web 实例，本沙箱未跑；单元层已覆盖真实执行路径（真实 HTTP + 真实子进程）。
