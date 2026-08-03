# F21 质量门 · 工作流触发器（Webhook / 定时 / Chat）

**Git 分支**: `feat/f21-workflow-triggers`
**日期**: 2026-07-31
**三道门**: `ddd-code-reviewer`（对抗式）· `ddd-phase-quality-gate`（结构 12 类）· `codebase-optimizer`（七维）

---

## Gate Status: PASS
`[P0: 0 | P1: 0 | P2: 0 (waived: 0) | P3: 0 (waived: 0)]`

全部通过；本轮发现并修复 P2×1 + P3×2，回归后 `dotnet build 0/0`、`dotnet test 354/354`、前端 `qa.mjs OVERALL PASS`。

---

## 1. ddd-code-reviewer（对抗式审查）

逐文件阅读 F21 全部新增/修改模块，按模块类型跑对应清单（API 控制器 → Section G；仓储/数据访问 → Section F；调度 BackgroundService → Section H2 + C；状态转移 → Section A 强制；通用 → Section Z）。

### Findings（已修复）

| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P2 | 分布式锁正确性 | `Scheduling/RedisDistributedLockProvider.cs` | `ReleaseAsync` 用无条件 `KeyDeleteAsync(key)`，TTL 过期后被他实例抢占、本实例释放时误删他实例锁（多实例竞态）。 | 改为令牌 CAS 释放：acquire 生成 `Guid` 令牌写入 Redis + 进程内 `_heldTokens`；release 用 Lua `if GET==ARGV then DEL`；降级放行路径不持有真实锁、释放直接跳过。接口 `(key,ttl)` / `(key)` 不变。 |
| P3 | 文档/行为漂移 | `WorkflowTriggers/GenerateWebhookTokenCommand.cs` | record 注释写「已存在则轮换」，实际为幂等复用（不轮换），与 handler 行为矛盾。 | 注释改为「幂等：已存在则复用现有令牌并确保启用、不轮换；不存在则新建」。 |
| P3 | 文档/行为漂移 | `WorkflowTriggers/InvokeWebhookCommandHandler.cs` | 注释写禁用映射为「403」，实际 `WebhooksController` 对 null 统一返回 404。 | 注释改为「已禁用（控制器映射为 404…不暴露存在性）」。 |

### 控制流分析
- **Webhook 调用链**：`WebhooksController.Invoke` → `InvokeWebhookCommand`（跨租户 `IgnoreQueryFilters` 按 token 查；null/非 Webhook→404；禁用→404；审计 pre-dispatch）→ `TriggerWorkflowCommand`（再次校验 workflow 归属 + Running 守卫 + 终态 `Reset()`）→ `OrchestrationPrimitive.RunAsync`。死路：无。未注册接口：无。
- **调度链**：`WorkflowScheduler`（每 30s `PeriodicTimer` 独立 scope）→ `RunDueScheduledWorkflowsCommand`（跨租户 `GetDueSchedulesAsync`；每触发器专属锁；先 `MarkScheduledRun`+`SaveChangesAsync` 推进 `NextRunAt` 再触发，失败也不死循环重触发）→ `TriggerWorkflowCommand`。`finally` 释放锁，对称。
- **Chat 链**：`Conversations/{id}/workflow-bindings[+{wf}]` → `BindConversationWorkflow`（会话+工作流双重租户校验 + 已绑定幂等）→ `TriggerWorkflowFromConversation`（三重校验：会话/工作流归属 + 绑定存在）→ `TriggerWorkflowCommand`。

### 测试覆盖
- App 层 18 例：GenerateWebhookToken（新建/复用/跨租户拒绝）、DisableWebhook（幂等禁用）、PutSchedule（nextRun 计算/禁用 null）、GetWorkflowTriggers（骨架/空值）、Bind（有效/跨租户拒绝）、TriggerFromConversation（委托/未绑定 null）、InvokeWebhook（未找到/禁用/委托）、TriggerWorkflowCommand（注入租户+还原上下文/未找到）。
- Api 契约 3 例：坏 token→404、GET triggers 形状、未鉴权→401。
- **联调冒烟 3 例（新增，真实宿主管线）**：Webhook 全生命周期（生成→调用 200→坏令牌 404→禁用→禁用调用 404→GET 显示 enabled=false）；Schedule（PUT→GET 显示、空 cron→400）；Chat（绑定→列表→chatBindingCount=1→触发 200→未绑定 404→解绑→chatBindingCount=0）。
- 缺失边界：无（空体/超大体/并发重复触发均有 guard 或 Running 守卫兜底）。

### 蓝图对齐
`features/workflow-triggers.md §7` 契约：POST webhook=幂等 get-or-create+re-enable、GET triggers=骨架+Chat 计数、Chat 仅信封/审计标签不设 `WorkflowTrigger` 实体——实现与契约一致，无漂移。

### Top 3 运行时风险（已闭环）
1. **多实例锁误删**（已修，P2）：`RedisDistributedLockProvider` 释放——现令牌 CAS，且 `TriggerWorkflowCommand` 的 `Running` 守卫双保险防重复触发。
2. **跨租户上下文泄漏**：`ITenantContext` 注册为 **Scoped**（`DependencyInjection.cs:265`），调度器每 tick / Webhook 每请求独立 scope，`OverrideTenantId` 不跨请求泄漏——已确认非 Singleton，P0 风险不存在。
3. **触发载荷污染工作流配置**：`TriggerWorkflowCommandHandler` 运行前合并信封、运行后 `UpdateContext(originalContext)` 还原——成功路径不持久化载荷；失败路径不调 `SaveChangesAsync`，DB 不被污染。

---

## 2. ddd-phase-quality-gate（结构审计 · 12 类全扫）

| 类别 | 结论 |
|------|------|
| G1 DI 注册缺口 | PASS — `ITenantContext`(Scoped)、`IDistributedLockProvider`(Singleton×2 条件注册)、`IScheduleCalculator`(Singleton)、`WorkflowScheduler`(HostedService) 全部注册；无 Application.Abstractions 接口漏注册。 |
| G2 DDD 层违规 | PASS — 接口在 `Application.Abstractions`/`Domain`，实现 `internal sealed` 在 `Infrastructure`；handler 在 `Application`。 |
| G3 EF 映射缺口 | PASS — `WorkflowTriggerConfiguration` + `ConversationWorkflowBindingConfiguration` 均 `IEntityTypeConfiguration`；迁移 `20260803014825_AddWorkflowTriggersAndBindings` + 快照。 |
| G4 硬编码值 | PASS — 轮询 30s / 锁 5min 为具名常量；令牌 32 字节随机；锁 key 模板字符串，无魔法数。 |
| G5 缺失 CancellationToken | PASS — 全部 async 方法透传 `ct`；锁 provider 可选 `ct`。 |
| G6 缺失修饰符 | PASS — 新增类均为 `internal sealed`。 |
| G7 并发风险 | PASS — `InMemoryDistributedLockProvider` 用 `ConcurrentDictionary` 且 acquire 时清理过期项；`RedisDistributedLockProvider` 新增 `_heldTokens` 并发安全；`WorkflowScheduler` 用 `PeriodicTimer` 并在 scope 内创建 DbContext。无 Singleton 持有无清理 grow-only 集合。 |
| G8 空守卫 | PASS — Domain 工厂 `ThrowIfNullOrWhiteSpace`；handler 对 null workflow/conversation/binding 返回 null（404）。 |
| G9 API 基础设施 | PASS — `WebhooksController` 有 XML 文档、`[EnableRateLimiting("WebhookAnonymous")]`、全局 ProblemDetails/ExceptionHandler。 |
| G10 蓝图漂移 | PASS — §7 契约与实现一致（见 §1 蓝图对齐）。 |
| G11 缺失 XML 文档 | PASS — Api 工程 CS1591 强制，新 controller ctor 已补；其余层非强制且 build 0 warning。 |
| G12 死代码/空心类 | PASS — `TriggerType`(Webhook/Schedule/Chat) 均被消费；新增 AuditActionType(WebhookInvoke/ScheduledRun/EnableTrigger) 均真实 emit；`IDistributedLockProvider`/`IScheduleCalculator`/`ITenantContext` 方法均有调用点。 |

---

## 3. codebase-optimizer（七维 · F21 增量）

| 维度 | 结论 |
|------|------|
| 架构 | PASS — DDD 分层、接口位置、DI 注册、租户隔离（`IgnoreQueryFilters` 仅跨租户查询、其余走全局过滤器）一致。 |
| 代码质量 | PASS — `internal sealed`、命名清晰、无桩代码（调度/锁/枚举全真实实现）。 |
| 正确性 | PASS — 三触发器路径双重租户校验、`Running` 守卫防重入、context 还原、幂等绑定/令牌。 |
| 测试 | PASS — 后端 354（App 18 + Api 3 契约 + 3 冒烟 + 其余）、前端 qa.mjs 44（含 i18n 对称）。 |
| 性能 | PASS — 调度每 tick 独立 scope（DbContext scoped），`InMemory` 锁字典 acquire 时清理；无 N+1 热点。 |
| 安全 | PASS — webhook token 32 字节 URL-safe base64（不可猜）、匿名端点限流、租户隔离、404 不泄露存在性。 |
| 工程化 | PASS — 迁移落地、`ValueGeneratedNever()` 规避 EF Guid 主键陷阱、build 0/0、前端 qa.mjs OVERALL PASS、中英 i18n 对称。 |

前端专项（XSS/`dangerouslySetInnerHTML`/硬编码密钥/`any` 泛滥）：`WorkflowTriggersDrawer` 与 `ConversationDetailPage` 仅用 AntD 受控组件 + `t()` 与 `getErrorMessage`，无危险 API，qa.mjs lint 0 error。

---

## 验证汇总
- `dotnet build src/AgentPlatform.sln` → **0 警告 / 0 错误**
- `dotnet test src/AgentPlatform.sln` → **354 passed**（Arch 9 · SpecFlow 41 · App 143 · Infra 123 · Integration 5 · Api 33）
- 前端 `node scripts/qa.mjs` → **OVERALL PASS**（typecheck / lint / build / unit 全绿，i18n-symmetry 通过）
- 新增联调冒烟 `WorkflowTriggersIntegrationTests` 3 例全绿（真实宿主 ASP.NET Core 管线）

## 结论
F21 三道质量门全部 PASS，无残留 P0/P1/P2/P3。可进入文档同步与提交收尾。
