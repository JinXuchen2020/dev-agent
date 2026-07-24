# F5 · 行动层落地（Agent 真正能做事）

> 设计文档（features/ 设计枢纽）。本 feature 为 🔴高风险（Phase 6 行动层），涉及后端工具执行 / 代码沙箱契约与"真实副作用"，按 feature-builder 护栏属「先出设计 + 范围选项，停下问人确认」类，本文档为待确认提案，未动手实现。

## §1 目标
让 Agent **真正在外部世界执行动作**——工具调用产生真实副作用、代码沙箱真实运行并回传 stdout/stderr，而非 `return 伪造成功`。这是「agent 编排平台」成立的核心。纯后端 feature，无前端路由/契约破坏性改动（前端节点绑定属 F7，见 §7）。

## §2 现状核验（已读真实代码，非臆测）
- **A1 工具执行层全空心**：三个 `IToolExecutor` 实现均直接 `return Task.FromResult(new ToolExecutionResult(true, "Executed ..."))` 伪造成功：
  - `NativeToolExecutor.cs:27` → `"Executed natively"`
  - `SkillPackageExecutor.cs:27` → `"Executed via SK Plugin"`
  - `McpClient.cs:27` → `"Executed via MCP"`
  - 调度层 `ToolCallingDispatcher.cs` 已健全：按 `ToolSource` 分发 → 查 `IToolRegistry` → 校验 `IsEnabled` → 调 `executor.ExecuteAsync(tool, parametersJson, ct)`。执行器只差"真干活"。
  - `ToolDefinition`（聚合）已带真实执行线索字段：`HandlerName`、`EndpointUrl`、`SkillPluginName`（`ToolDefinition.cs:43/58/63`），但执行器未消费。
- **A2 代码沙箱为桩**：`DockerCodeSandbox.cs:30/48` 同样伪造（`$"Executed {language} code successfully"` / `$"Command executed: {command}"`），且 `DependencyInjection.cs:146` 注册为 `ICodeSandbox`。**它依赖 Docker Desktop——本沙箱（dev-agent 执行环境）没有 Docker，无法运行/验证真实容器执行。**
- **A3 节点全家桶现状**：工作流 `IStepExecutor` 现仅 `KnowledgeRetrievalStepExecutor` / `CriticStepExecutor` / `AgentCallStepExecutor` 三种（grep 确认）。**没有 `Tool`/`Code` 节点执行器**——属于 F7 工作流平台化范畴，本次不新建节点，只打通下层执行能力（见 §7）。

## §3 拟改接口契约（后端）
### 3.1 A1 — `NativeToolExecutor` 真实化（必做）
- 消费 `tool.EndpointUrl` 发起**真实 HTTP 调用**（method 约定：`ParametersSchema` 未指定时默认 POST，body=`parametersJson`；GET 时参数拼 query）。
- 回传真实结果：`Output` = 响应体（截断至 `MaxOutputBytes`）；`Success` = 状态码 2xx；非 2xx / 超时 / 连接失败 → `Success=false` + `ErrorMessage` = 真实原因。
- 超时：复用 `SandboxSettings.HttpTimeoutSeconds`（默认 15s）；失败精准回打（符合 Phase 6「critic 循环 / 失败精准回打」范式）。
- `SkillPackageExecutor` / `McpClient`：本次**保留 stub 但加 `// TODO(Phase6)` 注记**（真实化需 SK runtime / 外部 MCP server，超出本次；backlog A1 仅要求"至少 NativeToolExecutor 接真实执行"）。
- 契约不变：`IToolExecutor` / `ToolExecutionResult` 接口签名保持，仅改 `NativeToolExecutor` 内部实现 → 调度层零改动。

### 3.2 A2 — `ICodeSandbox` 真实化（范围待 §7 确认）
- **进程级沙箱 `ProcessCodeSandbox`**（推荐，本沙箱可验证）：用 `System.Diagnostics.Process` 拉起 `python` / `node` 子进程执行 `code`，捕获真实 `stdout`/`stderr`/`ExitCode`/`DurationMs`，超时 kill。`SandboxResult` 已含这些字段，无需改接口。
- **Docker 沙箱**（backlog 原案）：`DockerCodeSandbox` 接 `Docker.DotNet` 真实容器执行——但**本沙箱无 Docker，无法运行验证**（见 §7 范围决策）。
- DI 注册改为**条件注册**：`Sandbox:Provider` = `Docker` | `Process`（默认 `Process`，使本沙箱 `dotnet test` 可验证）。`DockerCodeSandbox` 保留，待有 Docker 环境再切。

### 3.3 配置（新增，最小）
- `SandboxSettings`：`Provider`(Docker|Process, 默认 Process)、`TimeoutSeconds`(默认 30)、`HttpTimeoutSeconds`(默认 15)、`AllowedLanguages`(python|javascript|csscript…，默认 python,javascript)、`NetworkEnabled`(默认 false，进程沙箱禁网)、`MaxOutputBytes`(默认 64KB)。
- `appsettings.json` 加 `Sandbox` 节；无环境变量时走默认（不写死密钥/路径）。

## §4 数据模型
- 不新增表 / 聚合 / EF 迁移。`ToolDefinition` 已有 `EndpointUrl`/`HandlerName` 复用。
- 仅新增配置类 `SandboxSettings`（POCO + `IOptions`），无持久化。

## §5 验收标准（A1 / A2）
- **A1**：调用一个指向真实 HTTP 端点的 NativeTool → `Output` 为该端点真实响应体、`Success` 与状态码一致；端点返回 500 / 超时 → `Success=false` + 真实 `ErrorMessage`（证明"真实副作用"）。
- **A2（进程沙箱）**：`RunCodeAsync("print('hello')","python")` 返回 `Success=true, Stdout="hello\n", ExitCode=0`；语法错误代码返回 `Success=false, Stderr` 含真实报错；超时代码被 kill 并返回非零。
- **单测**：`NativeToolExecutor` 真实 HTTP 路径（用 `WebApplicationFactory`/本地 `HttpClient` 起测试端点，或 `MockHttpMessageHandler` 断言真实请求发出 + 响应回填）；`ProcessCodeSandbox` 真实跑 python 代码断言 stdout。两者均可本沙箱运行。
- **QA**：`dotnet build` + `dotnet test`（应用/基础设施测试工程）全绿；`node scripts/qa.mjs`（前端，本 feature 前端无改动 → 仅 typecheck/lint/build/unit，e2e 不增）。

## §6 质量门清单（嵌入本设计文档，Phase 5 消费）
- **P0（阻断）**：
  - 三执行器不再返回伪造成功字符串；`NativeToolExecutor` 真实 HTTP 调用且失败精准回打。
  - `ProcessCodeSandbox` 真实捕获 stdout/stderr/exitCode/超时；无 Docker 依赖即可跑测试。
  - 不引入安全退化的"任意代码/命令执行免审"；超时 + 默认禁网 + 语言白名单。
- **P1（高）**：
  - `IToolExecutor` / `ICodeSandbox` / `ToolExecutionResult` / `SandboxResult` 接口签名不变（向后兼容）。
  - `ToolCallingDispatcher` 零改动（仅执行器内部变真）。
  - 配置经 `IOptions<SandboxSettings>` 注入，不写死。
- **P2（中）**：
  - 真实执行路径有结构化日志（入参脱敏、出参长度、耗时）。
  - 单测覆盖 A1 真实路径 + A2 真实运行 + 失败/超时分支。
- **P3（低）**：
  - `SkillPackageExecutor` / `McpClient` 保留 stub 处加 `// TODO(Phase6): 真实化需 SK runtime / MCP server` 注记，避免误判为"已完成"。
  - backlog A3（Tool/Code 节点）明确 defer 到 F7，本文档不新建节点执行器。

## §7 风险与范围决策（🔴待用户确认）
本沙箱**无 Docker Desktop**，backlog A2 原案的 `Docker.DotNet` 真实容器执行**无法在本环境运行与验证**，而 feature-builder 硬门槛要求 QA/测试全绿。需用户确认本次范围：
- **范围 A（推荐）**：A1 真实 HTTP 工具执行 + A2 进程级沙箱（真实、可本沙箱验证、不依赖 Docker）。完整闭环，隔离性弱于容器但足证"真能做事"。
- **范围 B**：仅 A1 工具真实执行；A2 代码沙箱暂缓至有 Docker 的环境再按原案做。
- **范围 C**：A2 硬上 `Docker.DotNet` 真实容器代码（写真实实现），但本沙箱无法运行验证 → 留未验证代码，违背质量门"必须全绿"，不推荐。

> 另：A3「Tool/Code 工作流节点」当前不存在执行器，属 F7 平台化。本次不新建节点，仅打通下层执行能力；是否要把"新增 Tool/Code 节点执行器并接 ToolCallingDispatcher/ICodeSandbox"一并纳入，请在范围决策中指明。

## §8 质量门记录（实现后填）
- 8.1 ddd-code-reviewer：_（实现后填，须含"已核对 A1/A2 真实副作用验收"）_
- 8.2 ddd-phase-quality-gate：_（实现后填）_
- 8.3 codebase-optimizer：_（实现后填）_
