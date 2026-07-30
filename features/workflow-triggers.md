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

## 3. 数据模型与改动面
- **新增聚合** `WorkflowTrigger`（ITenantScoped）：`{ Id, WorkflowId, TenantId, Type(Webhook|Schedule|Chat), TriggerToken?, Cron?, Timezone?, Enabled, CreatedAt }` + EF 迁移（`Id ValueGeneratedNever()`）。
- `WorkflowScheduler`（`BackgroundService`，Infrastructure）：轮询 `IWorkflowTriggerRepository` 找到期 enabled Schedule 触发器，调用 `IMediator.Send(new RunWorkflowCommand(...))`；驻留单例、租户感知。
- Webhook 端点：`WebhooksController`（匿名 + token 校验，不依赖 cookie/JWT）。
- Chat 绑定：Conversation 聚合加 `BoundWorkflowId?`（轻量列 + 迁移）或独立关联表（待定 S2）。
- 审计：`AuditActionType` 增 `EnableTrigger/DisableTrigger/WebhookInvoke/ScheduledRun`。

## 4. 风险
- 🔴 高风险：后台调度基础设施（定时精度/多实例重复触发/租户扫描）、Webhook 匿名端点安全、Chat 触发与现有会话链路耦合。
- 缓解：调度 v1 单实例轮询 + `LastRunAt` 幂等（多副本下用 DB 行锁/乐观并发防重）；Webhook token 用 `Guid` 不可猜 + 限流。

## 5. 验收标准草案
- Webhook：生成 token→POST 携带 payload→新 execution 启动且 context 含 payload；错误 token→404；token 重置后旧 token 失效。
- 定时：配置 cron→调度到点自动启动 execution；禁用后不再触发；时区正确。
- Chat：绑定后用户发触发指令→工作流运行，结果回会话。
- 多租户：A 租户 webhook/schedule 不触发 B 租户工作流。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（待锁定）
- **S1** 定时调度基础设施：进程内 `BackgroundService` 轮询（v1）vs 引入 Quartz/外部 cron（后续）。
- **S2** Chat 绑定存储：Conversation 聚合加列 vs 独立 `ConversationWorkflowBinding` 表。
- **S3** Webhook 限流：v1 是否内置（建议先用现成限流中间件，若已存在）。
- **S4** 多实例调度防重：DB 行锁 vs 分布式锁（v1 单实例轮询 + `LastRunAt`）。
