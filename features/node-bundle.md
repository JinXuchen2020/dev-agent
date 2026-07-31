# F20 · 节点全家桶（Workflow 节点类型扩展）

> 状态：`doing`。来源：F7 工作流平台化 program 子项 **②**。分支：`feat/f20-node-types`。
> 定位：把 DAG 画布从「LLM/Agent/Tool/Code/Knowledge」扩展到生产级编排原语。**Tool/Code/Knowledge Retrieval 节点 executor 已在 F5 落地**（StepType.Tool=6 / Code=7 / Knowledge=5），本 feature 补其余类型 + 前端调色板/配置面板 + 运行时 executor + 编排器分支/循环引擎。
> **§6 决策已于 2026-07-31 锁定**（S1–S5 用户拍板，采用更完整的架构方案）。

## 0. 目标
让工作流能表达真实编排语义：HTTP 调用外部服务、条件分支、循环、变量读写、嵌套子工作流、延迟等待、人工审批门（HITL）。

## 1. 范围
**in**：
- 新增节点类型：`HTTP` / `Condition` / `Loop` / `Variable` / `SubWorkflow` / `Delay` / `UserInput(HITL)`
- 各节点前端调色板图标 + 配置面板 + 后端 executor（`IWorkflowExecutable` 经 `HandlesType` 路由，沿用 F5 模式）
- `WorkflowGraphSnapshot` / 导入导出（F7 ①）已能携带任意 `StepType`，本 feature 仅需让序列化与校验兼容新类型（补充 `ValidateGraph` 的拓扑规则）
- **S3 决策衍生**：新增 `HumanApproval` 聚合（EF 迁移 `AddHumanApproval`）+ 审批恢复端点，支撑 UserInput HITL

**out（本 feature 不做）**：
- 触发器（见 F21）、发布为 API（F22）、调试器（F25）单步能力 —— 仅提供节点原语，不提供触发/发布/单步 UI
- S5 的 Loop body 为「引用主图节点分组」，不做独立子图嵌套数据模型

## 2. 接口契约（后端）

### 2.1 StepType 枚举扩展（Domain，连续值，避免与现有冲突）
```
Http = 8, Condition = 9, Loop = 10, Variable = 11, SubWorkflow = 12, Delay = 13, UserInput = 14
```
后端路由按 `IStepExecutor.HandlesType` 解析（非 switch 枚举），仅前端 `StepType` 常量 + 映射需同步扩展。

### 2.2 节点配置结构（落 `WorkflowNode.ConfigJson`）
- `Http`：`{ method, url, headers, bodyTemplate, authRef? }`
- `Condition`：`{ expression }`（S2：Jint 沙箱表达式，变量引用 `artifacts['name']` / `blackboard.key` / `input`，返回布尔）
- `Loop`：`{ itemsSource, itemVariable, bodyNodeNames: string[] }`（S5：body 引用主图节点名列表，executor 内联迭代）
- `Variable`：`{ mode: set|get, name, value }`（`value` 支持字面量或表达式；`set` 写 Blackboard，`get` 读 Blackboard 并返回）
- `SubWorkflow`：`{ workflowId, inputMapping? }`（S4：触发独立 execution，记录子流 ExecutionLog id 引用）
- `Delay`：`{ durationMs }`（上限 30_000ms，防恶意长阻塞）
- `UserInput`：`{ prompt, approvalRole? }`（暂停等待人工恢复，S3）

### 2.3 HITL 审批恢复端点（S3，新增）
- `POST /api/v1/workflows/{id}/executions/{execId}/approvals/{approvalId}/resolve`
  - Body：`{ approved: bool, input?: string }`
  - 行为：加载 `HumanApproval`（租户校验）→ 置 `Approved/Rejected` + `SubmittedInput` + `ResolvedAt`；将对应 `UserInput` 节点的 `Result` 置为 `input`（或拒绝原因），节点置 `Completed`；调用 `IOrchestrationPrimitive.ResumeAsync` 续跑（跳过已完成节点）。
  - RBAC：与既有 workflow 端点一致（已认证租户用户可执行自己租户的工作流）。
- `HumanApproval` 聚合字段：`Id(Guid,ValueGeneratedNever)`、`TenantId`、`WorkflowId`、`ExecutionId`、`NodeName`、`Prompt`、`Status(Pending/Approved/Rejected)`、`SubmittedInput?`、`ResolvedAt?`、`CreatedAt`。

## 3. 数据模型与改动面
- `StepType` 枚举扩展（Domain，`WorkflowStepType.cs`）——后端路由不依赖 switch，回归面低；前端 maps 需扩展。
- `Blackboard` 改为**可变**（原不可变 record，`Set` 原地修改并返回 `this`）——使 `Variable` 节点跨节点读写生效；编排器单次运行维护单一 `Blackboard` 实例贯穿全程。
- `IWorkflowExecutable` 新增 7 个 executor 类（`Infrastructure/Workflows/`），经 `HandlesType` 注册。
- **编排器引擎改造（核心）**：
  - `SequentialOrchestrator.RunSequentialAsync` 增加 `skip` 集合：`Condition` 节点执行后，按分支结果计算「非选中分支可达子图」并入 skip（排除与选中分支/Start 可达重叠的 join 节点），线性遍历时跳过。
  - `Loop` 节点：executor 内部用注入的 `IWorkflowNodeRunner` 对 `bodyNodeNames` 逐 item 迭代执行（共享可变 Blackboard 携带 `itemVariable`），迭代后把 body 节点标 `Completed` 防主循环重复执行。
- 前端 `appStore` 节点调色板 + 7 个配置面板组件（`WorkflowCanvasPage`/`nodes/`）。
- `ValidateGraph` 增补：Condition 须有 `true`/`false` 两条带 Label 出边；Loop 的 `bodyNodeNames` 须全部存在且为图中节点；UserInput 可作末端等待（不强制出边）；SubWorkflow 的 `workflowId` 须非空。
- **新增聚合**：`HumanApproval`（Domain）+ `IHumanApprovalRepository` + EF 配置 + 迁移 `AddHumanApproval`。

## 4. 风险
- 🔴 高风险：枚举扩展（已证实后端无 switch 回归，仅需扩前端）、编排器分支/循环引擎改造（skip 集合 + 内联 body）、HITL Paused/Resume 链路、Jint 沙箱安全（仅暴露作用域变量 + Math，无 .NET 对象注入，带超时）。
- 缓解：枚举扩展先行 grep 确认无遗漏 switch；编排器改造保持现有线性遍历语义（非 Condition/Loop 节点行为不变）；Jint 默认不暴露宿主 API，表达式仅能访问显式注入的 `artifacts/blackboard/input/Math`；Delay 设硬上限。

## 5. 验收标准
- 各新节点可拖入画布、配置、存为工作流、执行产生正确下游路由与 IO。
- HTTP 节点真实出站（mock transport 单测）；Condition 按表达式真/假走不同分支（skip 集合生效，join 节点不被误跳）；Loop 按集合迭代 body 子图（itemVariable 注入 Blackboard，多轮后下游可见末轮值）；Variable 跨节点可读写；SubWorkflow 触发独立 execution 并回写子 ExecutionLog id 引用；Delay 实际等待；UserInput 暂停→审批恢复后继续。
- 导入/导出 JSON 携带新类型节点无丢失（复用 F7 ① 往返测试）。
- 前端 tsc 0 + qa.mjs 全绿 + 7 节点配置面板单测；HITL 暂停态出现审批弹窗可输入并恢复。

## 6. 决策（已锁定 · 2026-07-31）
- **S1** StepType 枚举扩展命名与起始值：`Http=8, Condition=9, Loop=10, Variable=11, SubWorkflow=12, Delay=13, UserInput=14`（沿用枚举预留注释）。
- **S2** 条件/循环表达式引擎：**嵌入式 JS 沙箱（Jint）**。新增 `IConditionEvaluator` + `JsConditionEvaluator`：在 Jint 引擎中运行表达式，仅注入 `artifacts`（上游 artifact 字典）、`blackboard`、`input`、`Math` 等安全作用域，带执行超时（默认 2s），不暴露任何 .NET 宿主 API，禁止任意代码副作用（无文件/网络/进程访问）。
- **S3** HITL 审批门：**新增 `HumanApproval` 聚合**（非复用 Paused 裸态）。`UserInput` 节点 executor 创建 `HumanApproval(Pending)` 并返回 `NeedsIntervention`；`SequentialOrchestrator` 已自动 `SetState(Paused)` 暂停；新增 `POST /.../approvals/{approvalId}/resolve` 端点接收人工输入、写回节点 `Result` 并调 `ResumeAsync` 续跑。含 EF 迁移 `AddHumanApproval`、租户隔离、审计。
- **S4** SubWorkflow 调用：**异步独立 execution 引用**。SubWorkflow 节点 executor 通过 `IOrchestrationPrimitive.RunAsync` 拉取目标 Workflow 并以独立 `ExecutionLog` 运行（独立执行上下文、可独立 Trace），父节点 Result 记录子流 `executionId` 引用（`{ childExecutionId, childWorkflowId }`），父工作流不阻塞等待子流输出、仅持引用。
- **S5** Loop body 表达：**引用主图节点分组**。Loop 节点的 `bodyNodeNames` 列出主图中参与循环体的节点名；Loop executor 对每个 item 将 `itemVariable` 注入共享 Blackboard，并用 `IWorkflowNodeRunner` 顺序执行 body 子图节点（body 节点为真实图节点，在线性主循环中被标 Completed 后跳过，避免重复执行）。不引入独立子图/嵌套数据模型。

---

## Phase Quality Gate Checklist（F20 · 节点全家桶）

> 嵌入于本 phase 文档（非独立文件）。三道质量门：`ddd-code-reviewer` + `ddd-phase-quality-gate` + `codebase-optimizer`（Phase 6+ 强制 PASSED）。本 feature 全部经三道门清零后，结论写入根 `.quality-gate.json`（`cleared: true`），报告见 `docs/quality/f20-node-types-gate.md`。

### 1. Pre-flight Version Audit
- [x] Jint 4.1.0 已安装，`JsConditionEvaluator` 实际 API 已核对（`Options.TimeoutInterval` 2s + `MaxStatements` 200_000，非训练数据臆测）
- [x] `dotnet build` 在新增代码前对既有代码通过（Phase 5 关门 0/0 基线）
- [x] StepType 枚举扩展（8–14）已 grep 确认后端无 `switch(StepType)` 回归点（路由走 `IStepExecutor.HandlesType`）

### 2. BDD Scenarios First
- [x] `F20NodeExecutorsTests.cs`（Infrastructure.Tests）：HTTP 真实出站（mock transport）、Condition 真/假分支、Loop 迭代（itemVariable 注入 + 末轮值可见）、Variable 跨节点读写、SubWorkflow 独立 execution 引用、Delay 实际等待、UserInput 暂停→恢复 全覆盖
- [x] `OrchestrationPrimitiveTests.cs`：补 RunAsync 已跟踪实体不重复 Add（F7 bugfix6 回归）
- [x] 前端 `workflowCanvasStore.nodeTypes.test.ts`：7 扩展类型映射 + addNode 默认配置

### 3. DDD Layer Rules
- [x] `IConditionEvaluator` / `IHumanApprovalRepository` 接口在 `Application.Abstractions` / `Domain.Repositories`
- [x] 实现类全部在 `Infrastructure`（6 executor + `JsConditionEvaluator` + `HumanApprovalRepository`）
- [x] DI 注册在 `Infrastructure/DependencyInjection.cs`（8 项：6 executor + IConditionEvaluator + IHumanApprovalRepository）
- [x] Domain 项目零外部 NuGet 依赖（Jint 仅用于 Infrastructure）
- [x] Api 层仅 `AddApplication()` + `AddInfrastructure()`，审批端点走 MediatR

### 4. DI Registration Completeness
- [x] `IStepExecutor` ×6 → Http/Condition/Variable/Delay/SubWorkflow/UserInput（Scoped）
- [x] `IConditionEvaluator` → `JsConditionEvaluator`（Scoped）
- [x] `IHumanApprovalRepository` → `HumanApprovalRepository`（Scoped）
- [x] `IServiceProvider` 解析验证（`dotnet test` 启动期 DI 校验）

### 5. Configuration-First
- [x] 超时/上限常量经 `Options` 或具名常量：`HttpStepExecutor.MaxTimeoutSeconds=30`、`DelayStepExecutor.HardCapMs=30_000`、`JsConditionEvaluator` 2s/200k
- [x] 无硬编码 GUID（仅既有 DatabaseInitializer/DevLoginEndpoint seed，超出 F20 范围）
- [x] HttpClient 用命名客户端 `workflow-http`（DI 注册，非 `new HttpClient()`）

### 6. EF Core Mapping Sync
- [x] `HumanApproval` 聚合 → `HumanApprovalConfiguration`（`internal sealed IEntityTypeConfiguration`，`ToTable("HumanApprovals")`，`ValueGeneratedNever()` Id）
- [x] `AppDbContext.HumanApprovals` DbSet 已加
- [x] 迁移 `20260731042445_AddHumanApproval` + `AppDbContextModelSnapshot` 已更新
- [x] `ITenantScoped` 全局 query filter 自动应用（租户隔离）

### 7. Concurrency & Lifecycle
- [x] `Blackboard` 为 per-run 可变实例（非 Singleton，无跨请求共享）
- [x] `DelayStepExecutor` 硬上限 30s，防恶意长阻塞
- [x] `JsConditionEvaluator` Jint 沙箱：默认不暴露宿主 API，仅注入 `artifacts/blackboard/input/Math` 作用域，带超时
- [x] `Loop` 内联迭代：body 节点 `Reset()` 每轮重跑，主循环标 Completed 防重复执行
- [x] `SubWorkflow` 独立 execution，父不阻塞等待，仅持引用（无级联失败）

### 8. Cross-Cutting Infrastructure
- [x] 审批端点 `GET/POST .../approvals[/.../resolve]` 走 MediatR，租户经 `_tenant.GetTenantId()` 校验
- [x] `ResolveApprovalCommand` 非 `ICommand<T>`（避免与 ResumeAsync 双 SaveChanges）；`ListApprovalsQuery` 非 `ICommand<T>`
- [x] `HumanApproval` 实现 `ITenantScoped`
- [x] 所有 executor `internal sealed` + `ArgumentNullException.ThrowIfNull` 守卫 + CancellationToken 透传
- [x] API 返回 DTO（`HumanApprovalDto` / `ApprovalDto`），非领域实体
- [x] 前端 `tsc --noEmit` 0 error、`eslint` 0 error、`vite build` 0 警告、`vitest` 全绿（`node scripts/qa.mjs` OVERALL PASS）
- [x] HITL 暂停态出现审批弹窗可输入并恢复（前端 qa 含 nodeTypes 测试）

### Incremental Gate Sequence
```
Module 1: 枚举 + 数据模型（StepType 8-14, HumanApproval 聚合, EF 迁移）
  - [x] build 0 warnings → test green → DI 审计 → 层审计
Module 2: 6 executor + Jint 表达式引擎
  - [x] build 0 warnings → test green → DI 审计 → 层审计
Module 3: 编排器分支/循环引擎（skip 集合 + 内联 body）
  - [x] build 0 warnings → test green → 控制流追踪
Module 4: HITL 审批恢复链路（端点 + ResolveApproval + 前端弹窗）
  - [x] build 0 warnings → 前端 qa 全绿 → 端到端复现
Module 5: 前端 7 节点调色板 + 配置面板 + 映射
  - [x] tsc/eslint/build/vitest 全绿
```

### Final Regression
- [x] 全量 `dotnet build` 0/0、`dotnet test` 全绿（Application 121+）
- [x] 前端 `node scripts/qa.mjs` OVERALL PASS
- [x] 后端起 5000 + 前端 dev 联调：HITL 暂停→审批恢复跑通（StubModelClient 种子账户 admin@acme.io）
- [x] 三道质量门 0 open findings（见 `.quality-gate.json` + `docs/quality/f20-node-types-gate.md`）
