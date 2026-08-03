# F21 · 工作流触发器（Webhook / 定时 / Chat）

> 状态：`open`。来源：F7 工作流平台化 program 子项 **③**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其定时调度基础设施与 Chat 触发入口）。

## 0. 目标
让工作流从「手动运行」变为「被动触发」：外部系统经 Webhook 调用、按计划（cron）自动运行、用户在会话（Chat）中触发。

## 1. 范围
**in**：
- **Webhook**：每个工作流可生成/重置一个 `triggerToken`，暴露 `POST /api/v1/webhooks/workflow/{token}`（携带 payload → 启动 execution，payload 注入初始 context）。
- **定时（cron）**：工作流可配置 cron 表达式 + 时区，由后台 `WorkflowScheduler`（`BackgroundService`）按租户扫描到期工作流并启动 execution。
- **Chat 触发**：在 `Conversation`/聊天页可「绑定工作流」，用户发消息（或特定指令）触发该工作流（复用现有消息/SSE 链路）。
- 三类触发器的启用/配置/停用 UI（工作流设置抽屉）。
- 多租户隔离（trigger 绑定 TenantId）+ 审计（EnableTrigger/DisableTrigger/WebhookInvoke/ScheduledRun）。

**out**：触发器自身的安全限流/防滥用（可后续 feature）、Chat 触发的高级意图识别（v1 用显式「/run <wf>」或按钮）。

## 2. 接口契约草案（后端）
- `POST /api/v1/workflows/{id}/triggers/webhook` → 生成/返回 `triggerToken`（Admin,Operator）；`DELETE` 重置。
- `POST /api/v1/webhooks/workflow/{token}` → 匿名可接受 token 即鉴权，启动 execution（payload→context），返回 `executionId`（限流待定）。
- `PUT /api/v1/workflows/{id}/triggers/schedule` body `{ cron, timezone, enabled }`（Admin,Operator）。
- `GET /api/v1/workflows/{id}/triggers` → 当前触发器配置（含下次运行时间）。
- Chat：`POST /api/v1/conversations/{cid}/bind-workflow` / `unbind`（复用对话鉴权）。

## 3. 数据模型与改动面（已锁定 S1–S4）

### 3.1 新增聚合 `WorkflowTrigger`（ITenantScoped）
一个触发器一行（`Type` 区分 Webhook/Schedule），字段按需可为空：
`{ Id(Guid, ValueGeneratedNever), WorkflowId, TenantId, Type(TriggerType: Webhook=0|Schedule=1), TriggerToken?(Guid 字符串, 仅 Webhook, 唯一不可猜), Cron?(string), Timezone?(string, IANA, 默认 UTC), Enabled(bool), LastRunAt?(DateTime, 仅 Schedule), NextRunAt?(DateTime UTC, 仅 Schedule, 预计算), CreatedAt, UpdatedAt }`
- Webhook 与 Schedule 复用同一聚合/表；一个工作流至多一个 Webhook 触发器与一个 Schedule 触发器（按 `(WorkflowId, Type)` 唯一）。
- `NextRunAt`（UTC，预计算）使调度扫描高效且时区正确；设置/更新/每次执行后由 Cronos 重算。
- EF 迁移 `AddWorkflowTriggers`（含 `ValueGeneratedNever`、触发器表 + `WorkflowTriggers` 唯一索引 `(WorkflowId, Type)`、Token 索引）。

### 3.2 新增实体 `ConversationWorkflowBinding`（ITenantScoped，S2 锁定 = 独立表）
- **S2 决策：独立关联表**（非 Conversation 加列），支持一个会话绑定多个工作流、易扩展。
`{ Id(Guid, ValueGeneratedNever), ConversationId, WorkflowId, TenantId, CreatedAt }`
- 注意：`Conversation` 已存在遗留 `WorkflowId?` 列（单绑定语义），本 feature 不以它为权威；Chat 触发器绑定统一走 `ConversationWorkflowBinding` 表（多绑定）。
- EF 迁移同上（与 3.1 同一次迁移）；配置 `ConversationWorkflowBindingConfiguration`（`ValueGeneratedNever` + `(TenantId, ConversationId)` / `(TenantId, WorkflowId)` 索引）。

### 3.3 调度基础设施（S1 / S4 锁定）
- `WorkflowScheduler : BackgroundService`（Infrastructure）：每 `SchedulerSettings.PollIntervalSeconds`（默认 30s）一轮；先尝试获取**分布式锁** `scheduler:tick`（TTL = 轮询间隔）防多实例重复触发；获取成功后用 `IWorkflowTriggerRepository.GetDueSchedulesAsync(nowUtc)`（`IgnoreQueryFilters()` 跨租户扫描 enabled 且 `NextRunAt<=now` 的 Schedule 触发器）；对每个到期项：在**子 scope 注入租户**（`ITenantContext.OverrideTenantId = trigger.TenantId`）后 `IMediator.Send(TriggerWorkflowCommand(...))`，再更新 `LastRunAt`/`NextRunAt` 并落库；最后释放锁。
- **S4 决策：完整分布式锁（Redis）**——`IDistributedLockProvider` 抽象：`RedisDistributedLockProvider`（复用已引的 `StackExchange.Redis`，`SET NX PX` 原子加锁，Redis 异常时降级为放行并告警）；`InMemoryDistributedLockProvider`（进程内，无 Redis 时回退，本地/测试可跑）。按 `Redis:ConnectionString` 是否配置选择注册。
- 后台租户注入：`ITenantContext`（scoped，含 `OverrideTenantId`）；`TenantProvider.GetTenantId()` 优先读它（HTTP 请求下为 null → 行为不变）。Webhook 匿名控制器同理在 scope 注入租户。

### 3.4 Webhook 端点
- `WebhooksController`（`[AllowAnonymous]` + `[EnableRateLimiting("WebhookAnonymous")]`，不依赖 cookie/JWT）：`POST /api/v1/webhooks/workflow/{token}` → 按 token 查启用的 Webhook 触发器 → 取 `WorkflowId`+`TenantId` → 注入租户 → `TriggerWorkflowCommand`（initialContext = `{"trigger":{"type":"webhook","payload":<body>}}`）→ 返回 `{ executionId }`。token 不存在/禁用 → 404。

### 3.5 审计
- `AuditActionType`（真实枚举位于 `Domain/Aggregates/AuditLogs/AuditLog.cs`，非已 Obsolete 的 `Enums/AuditActionType`）增 `EnableTrigger` / `DisableTrigger` / `WebhookInvoke` / `ScheduledRun`。

## 4. 风险
- 🔴 高风险：后台调度基础设施（定时精度/多实例重复触发/租户扫描）、Webhook 匿名端点安全、Chat 触发与现有会话链路耦合。
- 缓解：调度 v1 单实例轮询 + `LastRunAt` 幂等（多副本下用 DB 行锁/乐观并发防重）；Webhook token 用 `Guid` 不可猜 + 限流。

## 5. 验收标准草案
- Webhook：生成 token→POST 携带 payload→新 execution 启动且 context 含 payload；错误 token→404；token 重置后旧 token 失效。
- 定时：配置 cron→调度到点自动启动 execution；禁用后不再触发；时区正确。
- Chat：绑定后用户发触发指令→工作流运行，结果回会话。
- 多租户：A 租户 webhook/schedule 不触发 B 租户工作流。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（2026-08-03 已锁定）
- **S1 定时调度基础设施**：✅ 进程内 `BackgroundService` 轮询（v1，不引外部依赖；后续可平滑升级 Quartz）。
- **S2 Chat 绑定存储**：✅ **独立 `ConversationWorkflowBinding` 表**（用户拍板，多绑定、易扩展；不用 Conversation 加列）。
- **S3 Webhook 限流**：✅ **复用现有限流中间件**——新增 `WebhookAnonymous` 策略（按 token 分区固定窗口/令牌桶），`RejectionStatusCode=429`，零新依赖。
- **S4 多实例调度防重**：✅ **完整分布式锁（Redis）**——`IDistributedLockProvider` 抽象 + `RedisDistributedLockProvider`（SET NX PX），无 `Redis:ConnectionString` 时回退 `InMemoryDistributedLockProvider`，本地/测试可跑且不崩。

## 7. 接口契约（实现锁定）

### 7.1 WebhooksController（`[AllowAnonymous]` + `[EnableRateLimiting("WebhookAnonymous")]`）
| Method | Route | Body | 返回 | 说明 |
|---|---|---|---|---|
| POST | `/api/v1/webhooks/workflow/{token}` | 任意 JSON（payload） | `200 { executionId }` / `404` | token 不存在或 Webhook 禁用 → 404；启动 execution，payload 注入 initialContext。 |

### 7.2 WorkflowsController（新增端点；webhook/schedule 管理 `[Authorize(Roles="Admin,Operator")]`）
| Method | Route | Body | 返回 |
|---|---|---|---|
| POST | `/api/v1/workflows/{id}/triggers/webhook` | — | `200 { triggerToken }`（幂等：已存在则返回现有 token，否则生成） |
| DELETE | `/api/v1/workflows/{id}/triggers/webhook` | — | `200 { triggerToken }`（重置：旧 token 失效，返回新 token）|
| PUT | `/api/v1/workflows/{id}/triggers/schedule` | `{ cron, timezone?, enabled }` | `200 { cron, timezone, enabled, nextRunAt }` |
| GET | `/api/v1/workflows/{id}/triggers` | — | `200 { webhook:{triggerToken?,enabled}, schedule:{cron?,timezone?,enabled,nextRunAt?}, chatBindingCount }` |

### 7.3 ConversationsController（新增端点；`[Authorize]`，租户隔离）
| Method | Route | Body | 返回 |
|---|---|---|---|
| GET | `/api/v1/conversations/{cid}/workflow-bindings` | — | `200 WorkflowBindingDto[]`（含 workflowId/name）|
| POST | `/api/v1/conversations/{cid}/workflow-bindings` | `{ workflowId }` | `200 { id }` |
| DELETE | `/api/v1/conversations/{cid}/workflow-bindings/{workflowId:guid}` | — | `204` |
| POST | `/api/v1/conversations/{cid}/trigger-workflow/{workflowId:guid}` | — | `200 WorkflowDetailResponse`（Chat 触发动作，会话上下文注入 initialContext）|

### 7.4 DTO / 命令
- `TriggerWorkflowCommand(Guid WorkflowId, Guid TenantId, string? InitialContext)`：复用 `IOrchestrationPrimitive.RunAsync`，**不**实现 `ICommand<T>`（RunAsync 自管 per-step 持久化，避免双重 SaveChanges）。Handler 与 `RunExistingWorkflowCommandHandler` 同构：租户校验 → 终态/暂停先 `Reset()` → 注入 `InitialContext`（非空时 `wf.UpdateContext`）→ `RunAsync(wf, Sequential, ct)` → 写审计。
- `GenerateWebhookTokenCommand` / `ResetWebhookTokenCommand` / `PutScheduleTriggerCommand` / `GetTriggersQuery` / `BindConversationWorkflowCommand` / `UnbindConversationWorkflowCommand` / `ListConversationWorkflowBindingsQuery`。
- 调度内部命令 `RunDueScheduledWorkflowsCommand`（由 BackgroundService 派发，注入 override 租户后落库）。

### 7.5 前端
- 工作流设置抽屉：触发器页签（Webhook 生成/复制/重置 token；Schedule 配置 cron+时区+启用+显示下次运行时间；Chat 绑定说明）。
- 会话页：绑定/解绑工作流列表 + 「运行绑定工作流」按钮（Chat 触发）。
- `types/index.ts` + `api.ts` + `locales`（中英）对齐；camelCase。
