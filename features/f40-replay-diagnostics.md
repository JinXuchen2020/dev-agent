# F40 · 异常回放诊断入口 设计文档

> 来源：F43 评估门禁 · 延后项。风险：🟡 中风险（回放依赖 ExecutionLog 数据完整性）。
> 分支：`feat/f40-replay-diagnostics`（2026-09-02 自 `feat/f39-observability-alerting` 新建——用户指定基线）。
> 性质：纯新增**只读**分析端点 + 前端诊断视图，不改既有端点契约/鉴权/路由结构，无破坏性后端改动、不删数据 → 按 feature-builder 护栏可直接实现；因触及 UI，按硬约束 #7 配套 playwright-bdd E2E（CI 运行）。

## 1. 目标

从已有执行日志**重建**失败工作流的异常路径：定位失败节点、给出前后上下文、标注数据可用性，辅助人工快速定位根因。**只读，不重新执行任何步骤**。

## 2. 数据现状（调研事实，2026-09-02）

| 事实 | 位置 |
|---|---|
| `ExecutionLog`：Id/WorkflowId/WorkflowName/TenantId/WorkspaceId/`Status`(WorkflowState)/TotalSteps/StartedAt/CompletedAt + F30 `CheckpointData`(string?)/`CheckpointVersion` | `Domain/Aggregates/ExecutionLogs/ExecutionLog.cs:139-144` |
| `ExecutionLogEntry`：Id/StepName/StepOrder/`Status`(WorkflowState)/`Duration`(TimeSpan)/`Result`(string?)/`ErrorDetail`(string?)/TokensIn/TokensOut/`NodeType`(StepType?)/StartedAt/CompletedAt。**无独立 Input 字段** | `ExecutionLogEntry.cs:15-68` |
| Entries 以 `OwnsMany` 映射到独立表 `ExecutionLogEntries`；Result/ErrorDetail 为 `text` 不截断；NodeType 以 int? 存 | `Persistence/Configurations/ExecutionLogConfiguration.cs:57-99` |
| 失败路径**确实写 ErrorDetail**（result=null）；成功路径填 nodeType/tokens | `Application/EventHandlers/StepFailedEventHandler.cs:61-75`、`StepCompletedEventHandler.cs:62-76` |
| 仓储全部 `Include(Entries)`；已有详情查询 `ExecutionLogDetailResponse` | `ExecutionLogRepository.cs:24,32,48`；`GetExecutionLogDetailQuery.cs:25-66` |
| 控制器现有 3 端点（列表 / 详情 / steps），路由前缀 `api/v1/execution-logs` | `Api/Controllers/ExecutionLogsController.cs:43,73,97` |
| F30 检查点 `ExecutionCheckpoint{ Blackboard, ExecutionOrderIndex, StepStates[] }` **仅保存最新一份快照**（覆盖写），非 per-step 历史 | `Infrastructure/Workflows/SequentialOrchestrator.cs:981-1000` |
| 既有「从持久化数据重建 Blackboard」范式：`DebugDtos.ToBlackboard(...)` | `DebugResumeCommand.cs:26` 等 |
| 前端详情页为 Card + Descriptions + 单 Table，**无 Tabs** | `Web/src/pages/ExecutionLogDetailPage.tsx:91-102` |
| BDD 基座：后端 `ExecutionLog.feature`(6 场景) + `ExecutionLogSteps.cs`；前端 16 个 feature 无 execution-log 专属，`createBdd(test)` 用法见 `agent.steps.ts:1,5` | `SpecFlowTests/Features`、`Web/e2e` |
| 旧行降级判据：`NodeType == null`（F24 迁移 `nullable: true`）、tokens 默认 0 | `Migrations/2026080573534_ExtendExecutionLogEntry.cs:15-33` |

## 3. 能力边界（诚实声明，不夸大）

1. **per-node 输入不可得**：Entry 只存 `Result`。回放的「输入」以**同一步的 Result 之外的上下文**呈现：节点序列 + 前序节点 Result（作为推断输入来源），明确标注 `inputInferred=true`，绝不伪造独立输入快照。
2. **per-step Blackboard 历史不可得**：F30 只保留**最后一次**检查点。故报告提供 ① `contextSnapshot`（末次 Blackboard 快照，来自 `CheckpointData`，解析失败/无检查点时为 null 并给原因）与 ② 每节点的时间/耗时/状态/错误链。**不声称能重建每一步的上下文**。
3. 旧数据（F24 前）`NodeType=null`、tokens=0 → 报告以 `dataGaps` 列表如实标注缺哪类信息，前端灰显而非隐藏（避免「看起来没失败节点」的误判）。

## 4. 接口契约（camelCase）

`POST /api/v1/execution-logs/{id}/replay` — `[Authorize]`，只读（不改任何状态、不触发执行）。

```jsonc
// 200 ReplayReport
{
  "executionLogId": "guid",
  "workflowId": "guid",
  "workflowName": "wf",
  "overallStatus": "Failed",              // WorkflowState 原样（数值枚举 → 与既有 API 约定一致）
  "startedAt": "...", "completedAt": "...",
  "totalSteps": 3,
  "nodes": [
    { "stepOrder":0, "stepName":"Start", "status":"Completed", "nodeType":0, "isFailure":false,
      "startedAt":"...", "completedAt":"...", "durationMs":12,
      "result":"...", "resultTruncated": true, "resultLength": 4820,
      "previousResult":"...", "inputInferred": true,
      "errorDetail": null, "tokensIn": 120, "tokensOut": 40 }
  ],
  "failurePath": { "firstFailedStepOrder": 2, "failedStepNames": ["Step C"], "failedCount": 1 },
  "contextSnapshot": { "available": true, "source": "F30-final-checkpoint",
                       "variables": { "k": "v" }, "checkpointVersion": 5,
                       "executionOrderIndex": 2,
                       "note": "仅末次检查点，非 per-step 历史" },
  "dataGaps": ["nodeType-missing-legacy-rows", "no-input-snapshot"]
}
```

- 日志不存在或**跨租户** → **404**（与既有 `GetExecutionLogDetail` 一致语义，不暴露存在性）。
- Result/ErrorDetail 长文本截断至 4000 字符（`resultTruncated` + `resultLength` 标注），避免诊断端点拖出 MB 级响应；`ErrorDetail` 同样截断并标注。

## 5. 实现分层

- Application：`ReplayExecutionCommand(Guid ExecutionLogId)` → `ReplayReport?`（handler 经 `IExecutionLogRepository.GetByIdAsync`，**显式校验 `log.TenantId == tenantProvider.GetTenantId()`**，与既有仓储过滤器一致）；DTO 置 `ExecutionLogs/Commands/ReplayExecution/`。检查点解析复用 `System.Text.Json` + 容错（损坏 JSON → `contextSnapshot.available=false` + 原因），不动 F30 代码。
- Api：`ExecutionLogsController` 增 `Replay` action（`[Authorize]`、`[HttpPost("{id:guid}/replay")]`），null → 404。
- 前端：`ExecutionLogDetailPage` 引入 `Tabs`——「步骤」（既有表格原样保留）+「回放诊断」（失败路径时间线 Steps/Timeline + 每节点可展开详情 + Blackboard 变量表 + dataGaps 告警条）；`types/index.ts` + `api.ts` 补 `ReplayReport` 与 `replayExecution(id)`；i18n 中英对称。
- BDD E2E：`e2e/features/execution-log-replay.feature` + `steps/executionLog.steps.ts`（后端 BDD 亦加 replay 场景：失败日志→报告标注失败节点；成功日志→无失败；不存在→404）。

## 6. 验收标准

1. 失败日志 → 报告含完整节点序列、`failurePath` 指向真实失败步、失败节点 `isFailure=true` 且带 ErrorDetail。
2. 成功日志 → 节点序列完整、`failedCount=0`、`failurePath.firstFailedStepOrder=null`。
3. 不存在 / 跨租户 id → 404。
4. 旧数据（NodeType=null、无 checkpoint）→ 正常返回且 `dataGaps` 如实标注，不抛。
5. 只读：回放端点不写任何状态（无 SaveChanges、无审计写）。
6. build 0/0 + 全量 `dotnet test` 0 失败（既有豁免不变）+ 前端 tsc 0 error + vitest + vite build。
7. 三道质量门全绿；`.quality-gate.json` 推进 `f40-replay-diagnostics`；质量报告 `docs/quality/f40-replay-diagnostics-gate.md`。
8. 触及 UI → 新增 playwright-bdd feature + steps（CI 运行，本地不跑），并补后端 BDD 场景。
9. 文档同步：CHANGELOG、`appendices/api-spec.md`、backlog F40 done。

## 6b. 决策（已锁定，2026-09-02 用户拍板）

- **RBAC = `[Authorize(Roles="Admin,Operator")]`**（与同级 `GET /{id}`、`GET /{id}/steps` 一致；backlog 原文的裸 `[Authorize]` 会比同级更宽，不采）。
- **一并补齐既有越权面**：`ExecutionLogRepository.GetByIdAsync` 不带租户过滤，且 `GetExecutionLogDetail` / `GetExecutionLogSteps` 两个既有端点的 handler 也未校验 `log.TenantId` → 任意租户的 Admin/Operator 持 GUID 即可读他租户执行日志（**F40 之前就存在**，非本 feature 引入）。按用户决策一并加显式租户校验：两个既有查询增加 `TenantId` 入参与归属比对（不匹配 → null → 404，与既有「不暴露存在性」语义一致），新回放端点同规则；`QueryStepsAsync` 亦按租户收口。

## 7. 风险与缓解

- 🟡 数据不全被误读为「没有失败」→ `dataGaps` + 前端灰显告警条显式区分「无失败」与「信息缺失」。
- 🟡 长 Result 拖爆响应 → 固定截断 + 长度回传；确需全文时走既有详情端点。
- 🟢 只读、无副作用；跨租户由 handler 显式校验双保险。
- 🟢 不动 F24/F25/F30 既有代码，仅复用其数据格式。

## 8. 审查修复记录（ddd-code-reviewer · 2026-09-02）

| 严重度 | 位置 | 问题 | 修复 |
|---|---|---|---|
| P1 | ReplayExecutionCommand.cs handler | `request.TenantId == Guid.Empty` 兜底分支无测试锁定，未来可能被改成回落 `GetByIdAsync`（无过滤读取）复发越权面 | 新增回归测试 `Guid_Empty_TenantId_Falls_Back_To_Ambient_Tenant_Not_To_Unfiltered_Read`：断言 Guid.Empty → ambient 租户（fail-closed）且只调 `GetByIdForTenantAsync` |
| P1 | ReplayExecutionCommand.cs handler | 响应无条目数上限：循环展开的日志条目无上界，节点列表（含 `input` 复制前序 `output`）可拖出数十 MB 响应 | 加 `MaxNodesInReport=500` 封顶呈现 + `MaxFailedStepNames=50`；失败统计仍基于全量条目；新缺口码 `report-nodes-capped`（前端中英灰显文案）+ 测试锁定截断不失真 |
| P1 | ReplayExecutionCommand.cs handler ↔ WorkflowStartedEventHandler | `missingStepCount = TotalSteps - 条目数` 在生产路径恒为 0（建档 totalSteps:0 且聚合 `init` 不可变）→ 「无缺失」是假健康 | 新缺口码 `total-steps-unregistered`：`TotalSteps<=0` 时显式声明不可判 + XML 契约注明 + 测试锁定 |
| P1 | GetExecutionLogDetail/Steps 两 handler | 既有查询的租户收口接线（必须走 `GetByIdForTenantAsync`/`IsOwnedByTenantAsync`）无 handler 级测试，可被无声回退到无过滤路径 | 新增 `ExecutionLogTenantQueryHandlerTests.cs` 两测试锁定：跨租户 → null、`GetByIdAsync`/裸 `QueryStepsAsync` 零调用、非本租户不进分页查询 |
| P2 | ReplayPanel.tsx 结论 Alert | 「failedCount=0 即 success」对 暂停/回滚/执行中/空路径 报「整条路径均为成功态」= 假健康（违背 §3/§7 诚实性） | 三态判定：failed>0→error；全部 Completed→success；否则 info 新文案 `noFailuresPartial`；vitest 增 假健康回归用例（成功夹具改为全 Completed） |
| P2 | ExecutionLogDetailPage.tsx | SSE 刷新详情但回放报告保持陈旧（日志推进后 failedCount/路径过期仍呈现） | onmessage 收到进度事件即失效已加载报告（`setReplay(prev => prev ? null : prev)`），回放 Tab 按需 effect 自动重取；错误态不自动重取（防风暴） |
| P3 | ReplayExecutionCommand.cs XML doc | `Input` 注释称「首节点为执行上下文」与代码（返回 null）漂移；ReplayDataGaps 内游离空行 | 注释改为如实描述（首节点无推断来源为 null）；格式清理 |
| P3 | locales stepType zh/en | 表只到 10，StepType 11..15（变量/子工作流/延迟/人工审批/自主）落 `#n` 兜底 | 补齐 11..15 中英对称（zh 用「自主智能体」以过 i18n-symmetry 门） |
| P2 | ReplayPanel.tsx 时间线 Collapse | 质量门：`Collapse` item key=`String(node.stepOrder)`，但循环执行下多条节点共享同一 StepOrder（handler 已用 `ThenBy(StartedAt)` 消歧），key 不唯一 → Collapse `activeKey` 串档（展开一个连带展开同序项）+ React 重复 key 告警 | key 改 `${node.stepOrder}-${index}`（报告数组顺序按 StepOrder+StartedAt 确定，索引稳定）；`vitest` ReplayPanel 8/8 仍绿 |

**未修/仅记录（不构成缺陷或越界）**：e2e 硬编码种子 GUID/步名与 `IntegrationConstants` 跨语言重复（已有注释锚定，漂移建议由代码生成解决，不强制）；`QueryStepsAsync` 本身仍不带租户谓词（唯一调用方已前置 `IsOwnedByTenantAsync` 且有 EF 级测试 `ExecutionLogTenantScopeTests` 锁跨租户不可读）；checkpoint `Blackboard` 值经 `GetRawText` 兜底（F30 落库恒为字符串字典，可接受）；`GetByIdAsync` 保留为「可信内部路径」并由 EF 测试明示其无过滤性质（生产零调用，防回归护栏在）；回放节点排序键 `StepOrder+StartedAt` 对循环同序条目稳定；`inputInferred` 恒 true + 恒定 `input-snapshot-unavailable` 缺口码为设计契约本身。

**验证**：`dotnet build AgentPlatform.sln` 0 警告 0 错误；`AgentPlatform.Application.Tests` 284/284 通过（+5 新用例）；`AgentPlatform.Infrastructure.Tests` 175 通过 / 8 跳过（既有 Docker 门槛）；`AgentPlatform.SpecFlowTests --filter ExecutionLog` 8/8 通过；`ArchitectureTests` 9/9；前端 `tsc --noEmit` 0 error、`vitest` 50 通过 + 2 处既有豁免（i18n「搭建 Agent 团队」、AgentsPage contract）、ReplayPanel 8/8、`bddgen` 正常生成。

## Quality Gate Checklist

> 由 `ddd-phase-quality-gate`（Mode 3 = audit + checklist）生成，**内嵌于本文档**，绝不另建 checklist 文件。
> 范围 = 本 feature diff（`git diff feat/f39-observability-alerting`）。审计基线：12 类全跑，findings 见 §8 修复记录与本节末尾「Gate Status」。

### 1. Pre-flight Version Audit（前置版本核对）

- [x] 无新增 NuGet 包：复用 MediatR / System.Text.Json / antd / @ant-design（版本随基线锁定）
- [x] `IRequest<T>` vs `ICommand<T>` 选择经确认：回放为纯只读，采 `IRequest<ReplayReport?>` 以**避开** UnitOfWorkBehavior 的 SaveChanges（见 ReplayExecutionCommand remarks）
- [x] 复用既有列：`ExecutionLog.CheckpointData`/`CheckpointVersion`（F30）、`ExecutionLogEntry.NodeType`/`TokensIn`/`TokensOut`/`ErrorDetail`（F24），无契约变更

### 2. BDD Scenarios First（BDD 先行）

- [x] 后端 `ExecutionLog.feature` 新增 2 场景：失败日志→报告标注失败节点+末次快照+入参缺口+缺失 404；成功日志→无失败
- [x] `ExecutionLogSteps.cs` 真 HTTP+真 DB 步骤 + `ReplayReportDto`（camelCase）断言节点/失败路径/上下文/缺口
- [x] 前端 `execution-log-replay.feature` + `executionLog.steps.ts`（CI 运行，本地不跑）：Tabs 切换、时间线、失败标注、缺口披露、节点展开
- [x] 边界场景：不存在→404、旧数据（NodeType=null/tokens=0）→ 缺口不抛、损坏检查点→降级、循环→截断不失真、Guid.Empty 兜底

### 3. DDD Layer Rules（分层铁律）

- [x] `ReplayExecutionCommand`/`ReplayReport`/`ReplayNodeView`/`ReplayContextSnapshot`/`ReplayFailurePath` 置于 `Application/ExecutionLogs/Commands/ReplayExecution/`
- [x] 仓储新契约 `GetByIdForTenantAsync`/`IsOwnedByTenantAsync` 定义于 `Domain/Repositories/IExecutionLogRepository`
- [x] 实现在 `Infrastructure/Persistence/Repositories/ExecutionLogRepository`（`internal sealed`，既有注册覆盖，无需新增 DI）
- [x] Application 未引用 Infrastructure（仅 Domain + Application.Abstractions）；Domain 零外部包
- [x] Controller 仅经 `IMediator` 分发业务（注入 `ITenantProvider` 仅为把 ambient 租户传入命令，未旁路 MediatR）

### 4. DI Registration Completeness（DI 注册完整性）

- [x] `ReplayExecutionCommandHandler`（`internal sealed`）经 `RegisterServicesFromAssembly` 自动注册（MediatR 程序集扫描），无手工注册缺口
- [x] 新仓储方法随 `ExecutionLogRepository : IExecutionLogRepository` 生效，生命周期不变（Scoped）
- [x] `ITenantProvider` 已在基线注册，回放/详情/steps handler 构造注入可用

### 5. Configuration-First（配置优先 / 魔法数）

- [x] `MaxTextLength=4000` / `MaxNodesInReport=500` / `MaxFailedStepNames=50` 为 `private const`，带中文 XML 说明 + 前端 i18n 文案锚定（「前 500 个节点」），属协议级 DoS 防护上限，非每租户可调项 → 见 Gate Status P3-waiver
- [x] 无硬编码 GUID/URL/模型名/预算进入生产路径（e2e 种子 GUID/步名为测试代码，且与 `IntegrationConstants` 注释锚定，值精确匹配）

### 6. EF Core Mapping Sync（映射同步）

- [x] 本 feature 无新增聚合/VO、无 schema 变更（全部为既有列读取）→ 无需新 `IEntityTypeConfiguration`
- [x] 无遗漏迁移需求确认：`CheckpointData`/`CheckpointVersion`/`NodeType` 均为既有列
- [x] `dotnet build` 无 EF 映射告警

### 7. Concurrency & Lifecycle（并发与生命周期）

- [x] 无新增 Singleton / 可变共享状态；handler 无状态、纯函数式重建
- [x] 所有异步方法透传 `CancellationToken`（handler → `GetByIdForTenantAsync`/`IsOwnedByTenantAsync`/`QueryStepsAsync`）
- [x] 前端 effect 无重复/永不加载：回放 Tab 按需 effect 守卫 `activeTab && !replay && !loading && !error`；错误态不自动重取（防风暴）
- [x] SSE 失效逻辑：进度事件 `setReplay(prev => prev ? null : prev)` 令已加载报告过期，Tab 生效时重取；`es.onerror`/卸载 `close()` 收口

### 8. Cross-Cutting Infrastructure（横切基础设施）

- [x] 三端点租户收口全覆盖：详情 `GetByIdForTenantAsync`、steps `IsOwnedByTenantAsync`→（判定通过才）`QueryStepsAsync`、回放 `GetByIdForTenantAsync`；均为单查询带租户谓词，无「先无过滤读再比对」绕过
- [x] 不存在/跨租户统一 → null → 404，不暴露存在性
- [x] 回放端点确实只读：handler 无 `SaveChanges`；`Replay_Is_ReadOnly_Never_Persists` 锁 Add/Update 零调用
- [x] 响应 camelCase + 枚举按 int（无 JsonStringEnumConverter）与前端 `number` 类型一致；路由 `POST .../replay` 与既有只读 `POST .../diff` 风格一致
- [x] Swagger：controller action + handler DTO 均有 `/// <summary>` 中文注释，随既有 `GenerateDocumentationFile`/`IncludeXmlComments` 呈现
- [x] 新公开类型/成员中文 XML 注释齐备；`internal sealed`；无 `null!`、无 `StringComparison` 遗漏（OrdinalIgnoreCase 用于大小写不敏感 JSON 键）
- [x] i18n 对称：`ReplayDataGaps` 8 码逐一具备中英 `gaps.*` 且无孤儿码；`stepType` 0..15 中英齐；无 `dangerouslySetInnerHTML`；`Collapse` key 已加索引去重（见 Gate Status P2-fixed）
- [x] 无 `any`（前端全类型化）；`tsc` 0 error

### Incremental Gate Sequence（增量门序列）

```
M1 Domain 契约（GetByIdForTenantAsync/IsOwnedByTenantAsync）→ build 0/0 → Infrastructure.Tests(EF) 绿
M2 Application 回放 handler + 两既有查询收口 → build 0/0 → Application.Tests 绿（含租户回归锁）
M3 Api 端点（Replay，RBAC Admin,Operator）→ build 0/0 → SpecFlow --filter ExecutionLog 绿
M4 前端 types/api/Tabs+ReplayPanel + i18n → tsc 0 error → vitest 绿 → bddgen 生成
M5 文档同步（CHANGELOG/api-spec/backlog）→ 三道质量门 → .quality-gate.json 推进
```

### Final Regression（最终回归）

- [x] `dotnet build AgentPlatform.sln` 0 警告 0 错误
- [x] `dotnet test`（Application/Infrastructure/Architecture）全绿；IntegrationTests 需 `OPENAI__Key`（既有豁免）
- [x] 前端 `tsc --noEmit` 0 error、`vitest` 绿（既有 i18n「搭建 Agent 团队」/AgentsPage contract 豁免不变）、`bddgen` 正常生成
- [x] 无新增 P0/P1；P2 React key 已修；P3 缺口以 §8/本节记录为 waiver
- [ ] 端到端手动走一遍：详情页→回放 Tab→展开节点/缺口/快照（本地需后端；CI playwright-bdd 覆盖）

### Gate Status

**PASS** — P0:0 | P1:0 | P2:1（已修：ReplayPanel `Collapse` key 循环态唯一化）| P3:2（waived）。

P3 waivers：
- `MaxTextLength`/`MaxNodesInReport`/`MaxFailedStepNames` 未外置 IOptions —— 理由：协议级响应体积 DoS 防护上限，非业务可调项，外置徒增配置面；风险：需调大上限须改代码重部署（低）；目标期：如无运维诉求则长期保留。
- `ReplayReport`/`ReplayNodeView` 若干字段（`errorTruncated`、节点 `startedAt`/`completedAt`、快照 `source`/`checkpointVersion`/`executionOrderIndex`/`stepStateCount`、`overallStatus` 等）当前 UI 未渲染 —— 理由：只读诊断 API 契约完整性的显式字段，供任何消费方/工具取用，部分由后端 BDD（`source`/`executionOrderIndex`）断言；均已被 handler 落值，非 `.Empty` 蜜罐；风险：UI 未即时呈现；目标期：后续按需增补展示即可，不阻塞 F40。

## 9. CI E2E 修复记录（2026-09-03）

| 项 | 位置 | 问题 | 修复 |
| :--- | :--- | :--- | :--- |
| CI E2E 失败（15s 超时） | `e2e/steps/executionLog.steps.ts`、`e2e/features/execution-log-replay.feature` | 步骤等待 `IntegrationSeeder` 播种的失败执行日志，但**该 seeder 只在 SpecFlow 进程内运行**（`IntegrationAppFactory.InitializeAsync`）；前端 E2E 后端是真实 `dotnet run --environment Integration`，只执行 `DatabaseInitializer` 的 Integration 夹具（ApiKey + 工作流）→ 该日志在此进程内根本不存在。附带：该种子会把 `ExecutionLog.feature` 的「total count should be 50」变成 51。 | 改为**场景自造数据**：`POST /workflows/import`（只建不跑，已核 handler 不调 `RunAsync`）建图 → `POST /{id}/run` 恰好一次 → 列表按 workflowId 定位并断言「恰好 1 条日志」；同时删除 seeder 块与 `FailedExecutionLogId`/`FailedExecutionStepName` 常量（消除计数干扰与死码）。 |
| 连带修正 | 同上 | 原以为 Start→End 图就能产出日志条目；实测编排器把 `StepType.Start/End` 排除在可执行节点之外（`SequentialOrchestrator.cs:378`）→ **结构节点不写 `ExecutionLogEntry`**。 | 图中加入 `Variable` 节点（`mode=set` 纯内存、无 LLM；E2E 后端跑真实模型，必须避开模型节点），时间线断言只针对该被执行的节点。 |
| 夹具位置 | `e2e/steps/fixtures.ts` | 在步骤文件内 `base.extend()` 自定义 test 会让 `bddgen` 直接失败：`Can't guess test instance`（playwright-bdd 只认 fixtures 文件导出的那一个 `test`）。 | `replay` 夹具并入 `fixtures.ts` 的同一条 extend 链；步骤文件恢复 `import { test } from './fixtures'`。 |

本地校验：`npx bddgen` exit 0、`tsc --noEmit` 0 error、`vitest` 与基线一致；真实浏览器 E2E 依 CI 验证。

## 10. CI E2E 复跑修复记录（2026-09-04）

| 项 | 位置 | 问题 | 修复 |
| :--- | :--- | :--- | :--- |
| exactly-1 断言得 2（平台级根因） | `Infrastructure/DependencyInjection.cs` | `WorkflowStartedEventHandler` 被注册**两次**：`AddApplication` 的 `cfg.RegisterServicesFromAssembly`（MediatR 12.4.1 对 `INotificationHandler<>` 走 `addIfAlreadyExists=true` 分支＝普通 `AddTransient`，**非 TryAdd**，源码 `ServiceRegistrar.cs` 已核实）+ Infrastructure 对同一批 Application.EventHandlers 类型的显式 `AddScoped` → 每条通知处理两次，**每次 run 产生 2 条 ExecutionLog**。与 §9 注释中「POST /workflows 留下两条日志」的历史观察吻合（当时误归因为创建即跑）。 | 删除 Infrastructure 的 7 处显式 `INotificationHandler` 注册（5 个事件处理器 + `SemanticMemoryWriteBackHandler`×2 接口）；Infrastructure 程序集内无任何 `INotificationHandler` 实现（grep 证实），扫描为唯一注册源。StepCompleted/StepFailed 等处理器此前同样双跑，去重后单跑。 |
| combobox 点击被拦截 60s | `e2e/steps/workspace.steps.ts` | 有选中值时 antd 的 `.ant-select-selection-item` span 盖住 combobox input，Playwright 命中目标检查失败无限重试。该步骤首次在 CI 跑到（前次卡在「确认」按钮）。 | 两处 click 改 `click({ force: true })`——与 `credentials.steps.ts:28` 已验证先例同款。 |

校验：build 0/0；App 285 / Infra 175+8skip / Api 39 / Arch 9 全绿；bddgen exit 0、tsc 0 error；真实浏览器 E2E 依 CI 验证。质量门：`docs/quality/ci-e2e-2026-09-04-double-handler-gate.md`。

## 11. CI E2E 复跑修复记录（2026-09-04，第二轮：断言选择器）

前轮根因修复生效（exactly-1 通过、新建流程走通），两场景推进到新断言点后失败。**本轮先本地复现再修**（本地 `dotnet run --environment Integration` + 占位 `OpenAI__Key`（守卫只查非空）+ Edge 跑 playwright，两败与 CI 完全一致）。

| 项 | 位置 | 问题 | 修复 |
| :--- | :--- | :--- | :--- |
| 回放时间线断言 strict mode 双命中 | `e2e/steps/executionLog.steps.ts` | `getByText('E2E Set Var')` 同时命中「步骤明细」表格 td 与回放时间线 span（antd Tabs 非激活面板仍挂载 DOM）。 | 锚定 `.ant-tabs-tabpane-active` 内时间线 Collapse header（`getByRole('button', {name:/…/})`）。 |
| 节点详情「错误」栏命中隐藏表头 | 同上 | `getByText('错误').first()` 按 DOM 序命中隐藏「步骤明细」表的 th 列头（非激活 tab）。 | 锚定 `.ant-tabs-tabpane-active .ant-collapse-item-active` 内断言。 |
| 工作空间选项恒 hidden | `e2e/steps/workspace.steps.ts` | **本 antd 版本把 `role="option"` 挂在 rc-select 的无障碍镜像 div 上**（本地探针实测：宽 0、textContent=value GUID、`aria-label`=label、恒隐形）；真正可见选项在 `.ant-select-dropdown` 门户的 `.ant-select-item-option-content`（无 ARIA 角色）→ `getByRole('option')` 永远 hidden，断言必败。 | 选项断言/点击均改为 `.ant-select-dropdown .ant-select-item-option-content` + `filter({hasText})`。 |

本地校验：全新 `integration-e2e.db` 上两场景 **2 passed**；bddgen exit 0、tsc 0 error。诊断用一次性探针脚本已删除。
