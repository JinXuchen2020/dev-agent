# F12 · Tool/Code 节点全链路 e2e

> **状态**：doing（feature-builder 全流程 · 分支 `feat/f12-tool-code-e2e`）
> **优先级**：P3 · 🟢低风险（测试基础设施）
> **来源**：F5 行动层残差 ④（单元层已覆盖真实执行路径；含 Tool/Code 节点的端到端需后端+Web 实例，本开发沙箱未跑）。

## 1. 目标

起**真实后端 + 真实 Tool/Code 执行器**，跑一条含 `StepType.Tool`(真实 HTTP) 与 `StepType.Code`(真实 python/node 子进程) 节点的工作流，断言：

1. 运行响应中每个节点 `State == Completed`，且 `Code` 节点 `Result` 含 stdout、`Tool` 节点 `Result` 含真实 HTTP 响应体；
2. 运行后在 `ExecutionLog` 中回填的逐步 `Result` 同样包含上述真实输出（端到端回填验证）。

**前端联动（可选）**：按 backlog 标注为可选，本 feature 不新增 UI，不触发 playwright-bdd 硬约束；前端画布节点已在前序 feature 就绪。残留项在 §7 标注。

## 2. 接口契约（复用既有，无新增端点）

| 动作 | 方法 / 路由 | 鉴权 | 说明 |
|------|------------|------|------|
| 导入工作流（含图） | `POST /api/v1/workflows/import` | `Admin,Operator` | `ImportWorkflowCommand(Name, InitialContext, Nodes[], Edges[], TenantId)` → `WorkflowDetailResponse` |
| 运行工作流 | `POST /api/v1/workflows/{id}/run` | `Admin,Operator` | `RunExistingWorkflowRequest{Preset?}` → `WorkflowDetailResponse?`（200 / 404） |
| 执行日志列表 | `GET /api/v1/execution-logs` | `Admin,Operator` | `ExecutionLogListResponseDto{Items[],TotalCount}`，`Items[].WorkflowId` |
| 执行日志步骤 | `GET /api/v1/execution-logs/{logId}/steps` | `Admin,Operator` | `ExecutionLogStepsResponseDto{Items[],TotalCount}`，`Items[].Result` 即回填输出 |

### 复用数据模型
- `WorkflowNodeRequest(Id, StepType Type, Name, WorkflowNodePosition Position, Config?, AssignedAgentId?)`；`StepType` 枚举按整数序列化（`Start=0, End=1, Tool=6, Code=7`）。
- `WorkflowNodeResponse` 含 `State`(`WorkflowState`) 与 `Result`(string?)；`WorkflowState.Completed=3`。
- `ExecutionLogStepEntryDto(Id, StepName, StepOrder, Status(int), Duration, Result?, ErrorDetail?, ...)`。
- `ToolDefinition(Guid id, name, description, parametersSchema, handlerName, tenantId, ToolSource source=NativeTool, endpointUrl?, skillPluginName?)`，`IsEnabled` 默认 true；经 `IToolRegistry` 内存单例按名解析（**DatabaseInitializer 不种子，测试须自注册**）。

### 节点 config（JSON）
- **Code**：`{"code":"print('hello-from-code')","language":"python","timeoutSeconds":30}` → `Result = stdout`。
- **Tool**：`{"toolName":"bdd-echo-tool","parameters":{"httpMethod":"GET"}}` → 经 `ToolCallingDispatcher` → `NativeToolExecutor` 真实 GET `ToolDefinition.EndpointUrl` → `Result = HTTP 响应体`。

## 3. 关键设计决策

### 3.1 保留真实执行器的工厂变体（核心）
既有 `IntegrationAppFactory`（`AgentPlatform.SpecFlowTests`）为隔离外部 LLM 行为，**默认剥除全部真实 `IStepExecutor` 并换成 `ConfigurableStepExecutor`（假输出）**。F12 必须保留真实 `CodeStepExecutor`/`ToolStepExecutor`/`NativeToolExecutor`，否则 Code/Tool 节点不会真跑。

→ 解封 `IntegrationAppFactory`，抽出 3 个可覆写钩子（默认行为不变，既有 BDD 不受影响）：
- `protected virtual string DbPath => "test-integration.db";`（F12 改独立文件 `test-integration-f12.db`，避免与基工厂争用同一磁盘 SQLite）。
- `protected virtual bool StripStepExecutors => true;`（F12 覆写为 `false`）。
- `protected virtual Dictionary<string,string?> IntegrationConfiguration => {...};`（F12 覆写追加 `Sandbox:Provider=Process`，跳过 Docker 探测/镜像拉取，直接走进程沙箱跑 python）。

新增 `RealStepsIntegrationAppFactory : IntegrationAppFactory`（保留真实执行器，进程沙箱）。

### 3.2 本地回环 Tool echo 端点（无外部网络）
`NativeToolExecutor` 向 `ToolDefinition.EndpointUrl` 发真实 HTTP。为跨平台稳定（规避 Windows `HttpListener` 的 URL ACL 问题），用 **`TcpListener` 回环动态端口**实现最小 HTTP 响应器 `ToolEchoServer`：监听 `127.0.0.1:0` 取空闲端口，对任意 GET/POST 返回 `HTTP/1.1 200 OK` + JSON `{"echo":"ok","tool":"bdd-echo-tool"}`。测试向 `IToolRegistry` 注册 `bdd-echo-tool` 指向该 `BaseUrl`。

### 3.3 测试组织（Reqnroll BDD，复用既有 harness）
在 `AgentPlatform.SpecFlowTests` 内新增：
- `F12IntegrationHost` 静态单例（懒初始化 `RealStepsIntegrationAppFactory`，独立 DB 文件）。
- `Features/WorkflowCodeToolE2E.feature` + `Steps/WorkflowCodeToolE2ESteps.cs`。
- 场景：admin 登录 → 注册本地 tool → API 导入 Start/Code/Tool/End 工作流 → admin JWT 经 API run → 断言运行响应节点状态 Completed 且 Result 含真实输出 → 经 `execution-logs` 回读断言逐步回填。

### 3.4 JWT 跨工厂可移植
`AuthHelper.LoginAsync` 经基工厂登录取 JWT；F12 工厂覆写 `IntegrationConfiguration` 时复用基类的 `Security:JwtSecretKey` 与 `Tenant:DefaultTenantId`，故基工厂签发的 admin JWT 对 F12 工厂 API 同样有效 → 用基工厂 token 调 F12 客户端即可。

## 4. 验收标准
- [ ] `RealStepsIntegrationAppFactory` 保留真实执行器，`Sandbox:Provider=Process`，独立 DB 文件。
- [ ] `ToolEchoServer` 回环动态端口返回固定 JSON；`bdd-echo-tool` 注册进 `IToolRegistry`。
- [ ] 经 API import 含 Code/Tool 节点工作流成功（200，返回 id）。
- [ ] 经 API run（admin JWT）→ 200，`Code`/`Tool` 可执行节点 `State=Completed`，`Code` 节点 `Result` 含 `hello-from-code`，`Tool` 节点 `Result` 含 `bdd-echo-tool`；`Start`/`End` 控制标记节点编排器不解析执行器、合法保持 `Pending`（整体工作流 `CurrentState=Completed`）。
- [ ] `GET /execution-logs` → 定位该 workflow 的 log → `GET .../steps` → `Items` 含 Code 步 `Result` 含 `hello-from-code`、Tool 步 `Result` 含 `bdd-echo-tool`。
- [ ] `dotnet build` 0/0；`dotnet test`（仅 F12 场景）本地 python 可用应实跑通过；全量 `dotnet test` 0 失败。
- [ ] 三道质量门全 PASS。

## 5. 风险与缓解
- **R1 执行器被剥除**：经工厂变体保留真实执行器；若仍走假执行，节点 Result 不会含真实 stdout/HTTP → 断言失败，CI 可感知（已设计为防止"假绿"）。
- **R2 回环端口占用**：`TcpListener` 取 `0` 端口由 OS 分配空闲端口，规避固定端口冲突；`HttpListener` 的 Windows URL ACL 问题通过改用 `TcpListener` 规避。
- **R3 python 可用性**：本沙箱与 CI `ubuntu-latest` 均有 `python` 于 PATH；若运行环境无 python，Code 节点子进程路径不覆盖（与 F12 设计前提一致），属已知残留。
- **R4 DB 争用**：F12 工厂用独立 DB 文件名，与基工厂 `test-integration.db` 隔离。

## 6. 质量门清单（Phase 5 嵌入）

### P0
- [ ] `RealStepsIntegrationAppFactory` 不破坏既有 BDD（基工厂默认 `StripStepExecutors=true` 行为不变，`DbPath`/`IntegrationConfiguration` 覆写不影响现有场景）。
- [ ] F12 场景断言的是**真实** stdout/HTTP 响应，而非假执行器的占位输出。

### P1
- [ ] `ToolEchoServer` 资源释放（`Dispose` 停止监听 + 取消循环），无端口/句柄泄漏。
- [ ] 工厂变体重构为最小可覆写钩子，无重复逻辑。

### P2
- [ ] `IToolRegistry` 注册在场景级幂等（重复注册同名 tool 不报错/覆盖正确）。
- [ ] 断言对枚举整数值（Completed=3）与字符串模糊匹配，避免脆弱精确匹配。

### P3
- [ ] 前端 BDD E2E（拖节点→运行→断言画布状态）属可选，本 feature 不实现，残留标注。
- [ ] python 不可用环境的跳过策略（见 R3）。

## 7. 残留（非阻断，后续可立 feature）
- 前端 playwright-bdd E2E：在 Web 实例拖出 Tool/Code 节点、配置、运行、断言画布节点状态与输出面板（F12 backlog 标注「可选」）。
- 多租户 Tool 隔离在 e2e 层的覆盖（本 feature 单租户 T1 实证）。

## 8. 关联平台修复（F12 暴露的真实缺陷）
F12 首轮 e2e 发现：`POST /{id}/run` 重跑 DAG 工作流时，所有 `Code`/`Tool` 节点 `State` 仍为 `Pending`、`Result` 为空，但工作流整体 `Completed`——即静默走了**遗留 `Steps` 投影**而非真实 DAG。根因：`Workflow._isDag` 未做 EF 持久化，重跑从 DB 重载后 `IsDag` 复位为 `false`，`SequentialOrchestrator.PrepareContext` 据此 fallback 到 `wf.Steps`（遗留线性投影，节点 `Type=null` + `ConfigJson="{}"`），真实 DAG `Nodes` 从未被编排。

→ 修复：在 `WorkflowConfiguration` 映射 `IsDag` 列（`not null` 默认 false），新增迁移 `PersistWorkflowIsDag`（含 `#pragma warning disable IDE0161` 以符合 `TreatWarningsAsErrors` 的 file-scoped namespace 铁律）。修复后 F12 场景：Code 节点 `Result="hello-from-code\r\n"`、Tool 节点 `Result='{"echo":"ok","tool":"bdd-echo-tool"}'`、二者 `State=Completed`，端到端回填 `execution-logs` 同含真实输出。此为通用 DAG 重跑缺陷，对所有含节点工作流的 run 接口生效。
