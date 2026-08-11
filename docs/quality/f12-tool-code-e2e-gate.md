# F12 · Tool/Code 节点全链路 e2e — 质量门报告

> 阶段：`f12-tool-code-e2e` · 分支 `feat/f12-tool-code-e2e` · 日期 2026-08-11
> 前置质量门报告：[f34-dual-layer-sandbox-gate.md](../quality/f34-dual-layer-sandbox-gate.md)

## 0. 概要

F12 起**真实后端 + 真实 Tool/Code 执行器**，跑一条含 `StepType.Tool`（真实 HTTP）与 `StepType.Code`（真实 python 子进程）节点的工作流，断言端到端：节点 `State=Completed` 且 `Result` 含真实 stdout/HTTP 响应，且 `execution-logs` 回填同含真实输出。

三道质量门对 F12 增量均 **0 阻断**（3 项发现 = 2 项编译/断言修复 + 1 项真实平台缺陷修复，均已闭环）。

实测验收：
- `dotnet build` 0 警告 0 错误（修复 IDE0161 后）。
- F12 场景实跑通过：Code 节点 `Result="hello-from-code\r\n"`（真实 python stdout）、Tool 节点 `Result='{"echo":"ok","tool":"bdd-echo-tool"}'`（真实 HTTP 响应）、二者 `State=Completed`；`execution-logs` 回填同含两处真实输出。
- 全量 `dotnet test` 0 失败：Arch9 / App188 / Infra138+6skip / Integration5 / Api35 / **SpecFlow115**（114 既有 BDD + 1 F12；`IsDag` 修复未破坏既有 BDD）。

## 1. ddd-code-reviewer（对抗式评审）

增量范围：`IntegrationAppFactory` 最小解封 + `RealStepsIntegrationAppFactory`/`ToolEchoServer`/`F12IntegrationHost`/`F12IntegrationClient`/`WorkflowCodeToolE2ESteps`/`WorkflowCodeToolE2E.feature` + `WorkflowConfiguration.IsDag` 映射 + 迁移 `PersistWorkflowIsDag`。

**发现与即时修复：**

1. **【编译阻断 · IDE0161】** 新迁移 `PersistWorkflowIsDag.cs` / `.Designer.cs` 由 `dotnet-ef` 生成 block-scoped namespace，而仓库 `TreatWarningsAsErrors=true` 将缺省 file-scoped namespace 判为 error `IDE0161`。→ 两个文件顶部补 `#pragma warning disable IDE0161`（与既有迁移一致）。全量 `dotnet build` 0/0。
2. **【断言误判 · 控制标记】** 原 `ThenAllNodesCompleted` 强求所有节点（含 `Start`/`End`）`State=Completed`。但编排器对 `Start(0)`/`End(1)` 控制标记不解析执行器、合法保持 `Pending`（整体工作流 `CurrentState=Completed`）。→ 改为仅校验可执行节点（`Type is not (0 or 1)`，即 Code=7/Tool=6）`State=Completed`。
3. **【真实平台缺陷 · IsDag 未持久化】** F12 首轮 e2e 暴露：`POST /{id}/run` 重跑 DAG 工作流时所有 `Code`/`Tool` 节点 `State` 仍为 `Pending`、`Result` 空，但工作流整体 `Completed`——静默走了**遗留 `Steps` 投影**而非真实 DAG。根因 `Workflow._isDag` 未做 EF 持久化，重跑从 DB 重载后 `IsDag` 复位 `false`，`SequentialOrchestrator.PrepareContext` 据此 fallback 到 `wf.Steps`。→ `WorkflowConfiguration` 映射 `IsDag` 列（not null 默认 false）+ 迁移 `PersistWorkflowIsDag`。此为通用 DAG 重跑缺陷，对所有含节点工作流的 run 接口生效。

**资源生命周期核查：** `ToolEchoServer` 持 `TcpListener` 单例，`AcceptLoopAsync` 循环 + `Dispose` 取消 `_cts`/停止监听/等待循环(2s) 对称释放；`F12IntegrationHooks.[Before/After]TestRun` 与 `IntegrationHooks` 对称启停，无端口/句柄泄漏。VERIFIED。

## 2. ddd-phase-quality-gate（阶段结构门）

F12 增量 P0/P1/P2/P3 = 0 阻断。12 类审计：

- **DI 注册完整**：`RealStepsIntegrationAppFactory` 保留真实 `IStepExecutor` 集（`CodeStepExecutor`/`ToolStepExecutor`/`NativeToolExecutor` 等）+ `ProcessCodeSandbox` + `InMemoryToolRegistry` 单例；`Sandbox:Provider=Process` 覆写跳 Docker 探测/镜像拉取。
- **DDD 分层正确**：`Application`/`Infrastructure` 零反向引用；F12 host/client/steps 全在 `SpecFlowTests` 测试侧，不污染生产代码。
- **无新增聚合映射**：仅 `Workflow.IsDag` 列映射，属既有 `Workflow` 聚合字段持久化，非新聚合。
- **无硬编码业务值**：python/echo 端点/断言片段均测试内联，无生产硬编码。
- **ct 透传**：F12 测试无长调用链，执行器 `ct` 透传沿用既有。
- **并发安全**：`ToolEchoServer` 单例 + SpecFlow 单场景串行（`workers`/`fullyParallel` 受控）；`DetectPythonCommand` 取退出码不竞争。
- **空守卫齐备**：`DetectPythonCommand` 优先 `python` 回退 `python3` 再回退 `"python"`；`LastRun.Nodes` 非空断言。
- **无 API 契约变更**：复用 `import`/`run`/`execution-logs` 既有端点，无新增 controller/路由。
- **无蓝图漂移**：`features/tool-code-e2e.md` §8 已记录 `IsDag` 修复，与实现一致。
- **中文 XML 齐全**：新增类型均附中文 XML 注释。
- **无 Swagger 变更**：无新端点。
- **IsDag 迁移非破坏**：既有 `Workflow` 聚合追加可空默认 false 的列，老数据 `IsDag=false`（遗留 Steps 行为保持），新导入 DAG 持久化 true。

## 3. codebase-optimizer（七维 · 分析模式）

七维 0 阻断（分析模式，不建分支/不 push，遵守 feature-builder 硬约束）：

- **架构**：`IntegrationAppFactory` 抽 3 虚钩子最小解封，基默认行为不变；`RealStepsIntegrationAppFactory` 覆写保留真实执行器；DDD 分层正确。
- **代码质量**：统一中文文档；显式注释 `Start`/`End` 控制标记语义；`ToolEchoServer`/`DetectPythonCommand` 注释清晰。
- **正确性**：全量 `dotnet test` 0 失败（6 程序集，SpecFlow 115 含 F12）；F12 场景断言真实 stdout/HTTP（防"假绿"——若走假执行器占位输出，断言必失败）。
- **测试**：新增 `WorkflowCodeToolE2E.feature`（1 场景）+ `RealStepsIntegrationAppFactory`/`ToolEchoServer`/`F12IntegrationHost`/`F12IntegrationClient`/`WorkflowCodeToolE2ESteps`；`TcpListener` 回环动态端口规避 Windows `HttpListener` URL ACL。
- **性能**：F12 仅 1 场景，`Sandbox:Provider=Process` 跳过 Docker 守护进程探测/镜像拉取，单次运行 <2s。
- **安全**：本地回环 echo 端点无外部网络；仅测试 DB 文件；JWT 复用基工厂签发（共享 `JwtSecretKey`/`DefaultTenantId`），无密钥落库。
- **工程化**：`dotnet build` 0 警告 0 错误；`dotnet list package --vulnerable` 无新增 CVE；迁移含 `#pragma warning disable IDE0161`。

**留观 P3：**
1. CI `ubuntu-latest` 仅 `python3`（已 `DetectPythonCommand` 注入 `Sandbox:InterpreterPaths:python` 缓解；若镜像彻底无 python，Code 节点子进程路径不覆盖，属已知残留）。
2. 前端 playwright-bdd E2E（拖节点→运行→断言画布状态）属可选残留，F12 backlog 标注「可选」，不实现。

## 4. 验收对照

| 验收项 | 结果 |
|--------|------|
| `RealStepsIntegrationAppFactory` 保留真实执行器、`Sandbox:Provider=Process`、独立 DB | ✅ |
| `ToolEchoServer` 回环动态端口返回固定 JSON；`bdd-echo-tool` 注册进 `IToolRegistry` | ✅ |
| API import 含 Code/Tool 节点工作流成功（200，返回 id） | ✅ |
| API run（admin JWT）→ 200，Code `Result` 含 `hello-from-code`、Tool `Result` 含 `bdd-echo-tool` | ✅ |
| 各可执行节点 `State=Completed`（Start/End 控制标记合法 Pending） | ✅ |
| `execution-logs` 回填逐步 `Result` 含真实输出 | ✅ |
| `dotnet build` 0/0；F12 场景实跑通过；全量 `dotnet test` 0 失败 | ✅ |
| 三道质量门全 PASS | ✅ |

## 5. 关联平台修复（F12 暴露）

`Workflow._isDag` 未持久化 → DAG 重跑静默走遗留 `Steps` 投影。修复见 `features/tool-code-e2e.md` §8 与迁移 `PersistWorkflowIsDag`（含 `WorkflowConfiguration.IsDag` 映射）。
