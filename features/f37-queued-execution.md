# F37 · 队列化执行与水平扩展（Redis Stream 最小闭环）设计文档

> 来源：F30 执行持久化 · 延后项；F34 评估门禁 · 延后项。
> 风险等级：🔴 高风险（执行分发范式变更 + 多 worker 协调 + 消息可靠性）。
> 分支：`feat/f37-queued-execution`（2026-09-01 自 `feat/f36-agent-context-isolation` 新建——用户指定基线，F37 消费 F35 的 WorkspaceId 上下文与 F36 的 agent 会话隔离语义）。

## 1. 目标

把「进程内同步执行 + BackgroundService 轮询」升级为**可水平扩展的队列分发**：多 worker 实例从队列竞争消费执行任务，无状态执行引擎横向扩展；复用 F30 `RunningExecution` 租约防重复驱动；Redis 不可用时降级回进程内路径（fail-safe，行为与现状一致）。

按 backlog 建议分两阶段：**本 feature = 阶段① Redis Stream 最小闭环**；RabbitMQ 企业级实现独立排期（接口已抽象，后续只加实现）。

## 2. 代码现状（调研事实，2026-09-01）

| 事实 | 位置 |
|---|---|
| 运行端点请求内同步 await 全编排后返回整个 Workflow 聚合（**无队列/后台化**） | `Api/Controllers/WorkflowsController.cs:92-106,160-172`；`OrchestrationPrimitive.cs:95-180` |
| `RunningExecution`：Id=WorkflowId、State、HeartbeatAt、LeaseExpiresAt、InstanceId、CheckpointVersion、BlackboardSnapshot；`TryAcquireLease`（比对持有者，F31 修复）/`TryRenewLease`/`ReleaseLease`/`Complete` | `Domain/Aggregates/Workflows/RunningExecution.cs:16-49,123-215`；`IRunningExecutionRepository.cs:14-40` |
| `DurableExecutionSettings`：LeaseTtlMinutes=5 / CheckpointBatchSize / CheckpointMaxAgeSeconds（**无 30s 租约**，backlog 验收原文与现状不符，见 §5 D3） | `Application/Abstractions/DurableExecutionSettings.cs:14-27` |
| Hosted services 全清单：ExecutionLogCleanupJob / ApiKeyExpiryJob / **WorkflowScheduler**（30s PeriodicTimer：轮询到期触发器 + 恢复过期租约） | `Infrastructure/DependencyInjection.cs:475-481`；`WorkflowScheduler.cs:21-107` |
| Redis 基建已在：`ConnectionMultiplexer`（`Cache:Provider=Redis` 条件注册，`ConnectionStrings:Redis`→`Redis:ConnectionString`→localhost:6379，AbortOnConnectFail=false）+ `RedisDistributedLockProvider`（Lua SET NX PX，故障降级放行）；StackExchange.Redis 2.8.0 已引 | `DependencyInjection.cs:259-318`；`RedisDistributedLockProvider.cs:17-64`；`Infrastructure.csproj:27` |
| **全仓无 Redis Stream（XADD/XREADGROUP/XCLAIM）使用**——本 feature 从零接 | grep 0 命中 |
| 评估门禁 F34：`RunEvaluationGateCommand` 请求内同步委托 `RunEvaluationCommand`，逐用例串行、克隆影子工作流同步跑完 | `RunEvaluationGateCommand.cs:45`；`RunEvaluationCommand.cs:41-99` |
| 后台上下文注入点：`ITenantContext.OverrideTenantId` / `IWorkspaceContext.OverrideWorkspaceId`（webhook :50/:53、调度 :71/:73、API-Key :89、种子 :111）；**WorkflowScheduler 恢复路径未设 workspace override**（队列消费者必须补） | `TriggerWorkflowCommandHandler.cs` 等 |
| 前端：`runWorkflow` 同步等全量结果；SSE 进度订阅在 ExecutionLogDetailPage（`/workflows/{id}/progress`） | `api.ts:311-333`；`ExecutionLogDetailPage.tsx:62` |
| 测试基座：`IntegrationAppFactory` 移除全部 `IHostedService`（队列 worker 在 BDD 中天然禁用，直跑路径不变）；`Cache:Provider=Memory` | `IntegrationAppFactory.cs:125-129,66` |
| SkippableFact 先例：F9 Docker 门控（Infrastructure.Tests 已引 xunit.skippablefact） | `docs/quality/f9-docker-sandbox-gate.md` |

## 3. 架构（v1）

```
Api（enqueue 端点/触发器/调度器）──XADD──▶ Redis Stream "ap:exec-queue"（消费组 "ap-workers"）
                                                  │ XREADGROUP（每 worker 循环，Block=2s）
                                          ExecutionWorker（BackgroundService，N 实例）
                                                  │ 反序列化载荷 → 设租户/工作空间 Override
                                                  │ TryAcquireLease（F30 租约，双 worker 互斥）
                                                  ▼
                                          既有 OrchestrationPrimitive.RunAsync（引擎零侵入）
                                                  │ 完成 → XACK；失败 → XACK+重试计数 / 超限落 dead-letter stream
XCLAIM min-idle-time = 租约 TTL ──▶ worker 崩溃后 pending 消息重投（接管语义 = F30 租约 + 消息侧重投）
QueueBackend=InMemory（默认）→ IExecutionQueue 的进程内 Channel 实现（有界背压，同容器内 worker 消费）；
Redis 不可用 → 启动探测失败即降级 InMemory 并结构化告警（对齐 F34 双层沙箱 fail-safe 范式）。
```

### 3.1 抽象与实现

- Application：`IExecutionQueue`（`EnqueueAsync(ExecutionJob, ct)` + `DequeueAsync(ct)` 语义由实现决定）+ `ExecutionJob` 载荷 record：`WorkflowId, TenantId, WorkspaceId, TriggerType?, PayloadJson?, EnqueuedAt, Attempt, RequestingUserId?`。
- Infrastructure：
  - `RedisStreamExecutionQueue`（XADD MAXLEN ~ / XREADGROUP / XACK / XAUTOCLAIM 或 XCLAIM 回收；consumer name = 实例 Id；连接串复用既有 `IConnectionMultiplexer` 注册）；Redis 操作异常 → 记日志并回退（入队侧：抛错让 API 显式失败还是直跑降级？见 §5 D2）。
  - `InProcessChannelExecutionQueue`（`Channel<T>` 有界 256 背压，行为等价；默认后端）。
  - `ExecutionWorker : BackgroundService`（注册条件：`DurableExecution:QueueEnabled=true`）：循环 Dequeue → 设 `ITenantContext`/`IWorkspaceContext` Override → 复用 `TryAcquireLease` → `OrchestrationPrimitive.RunAsync` → 释放租约/ACK。
- `DurableExecutionSettings` 扩展：`QueueEnabled`(默认 false)、`QueueBackend`("InMemory"|"RedisStream")、`WorkerConcurrency`(默认 1)、`MaxAttempts`(默认 3)、`DeadLetterStream`。既有 `LeaseTtlMinutes` 复用为接管窗口。
- 触发器/调度链：`QueueEnabled=true` 时 `TriggerWorkflowCommandHandler` 与 `WorkflowScheduler` 到期触发改为**入队**（消息载荷含 TriggerType/Payload），worker 消费时按既有语义执行；`QueueEnabled=false` 全链路 = 现状零变化。

### 3.2 Api

- 新端点 `POST /api/v1/workflows/{id}/enqueue`（`[Authorize(Roles="Admin,Operator")]`，与 run 同权）：`QueueEnabled` 时入队返回 **202 Accepted**（body：`{workflowId, queued:true}`）；未启用队列返回 **409/400 明确提示**（防误用静默假成功——reviewer 视角的 stub 红线）。既有同步 `run` 端点**不动**（契约不变，前端零改动）。
- 运行状态可观测性沿用现有 `GET /workflows/{id}` + SSE progress（无新前端面）。
- 评估门禁（D4 决策）：A) 门禁保持**同步直跑**（其部署前阻塞语义天然要求同步，队列模式不改门禁）；B) 门禁改「入队 + 轮询等待结果」。**建议 A**（backlog 验收「异步执行→同步等待」以 A+文档说明满足：门禁在队列模式下仍可用=直跑路径始终存在）。

## 4. 验收标准

1. `QueueEnabled=false`（默认）：全链路行为与 F36 基线**逐测试**一致（既有全套测试零回归）。
2. `QueueEnabled=true` + `QueueBackend=InMemory`：enqueue 端点 202；worker 消费后工作流到达终态；有界队列满时入队返回 429/503 明确错误（不静默丢任务）。
3. Redis Stream 路径（`SkippableFact`，无 Redis 环境跳过；CI ubuntu + docker Redis）：入队→消费→租约互斥→ACK；双 worker 并发消费同一 workflow 仅一执行（另一 TryAcquireLease 失败即 XACK 跳过）；worker「崩溃」模拟（不 ACK + 超 idle）→ XCLAIM 重投被另一 worker 接管。
4. 超限失败：`Attempt >= MaxAttempts` → 进 dead-letter stream + 结构化日志 + 工作流状态 Failed（不毒化队列）。
5. 消息载荷租户/工作空间上下文：消费侧 Override 生效，跨租户消息绝不误执行（单测断言 Override 设置 + 租约 workflow.TenantId 校验）。
6. 触发器在队列模式下改投递且行为等价（webhook/schedule 经 worker 执行，ExecutionLog/审计不丢）。
7. build 0/0 + 全量测试 0 失败（既有豁免：SpecFlow LLM 用例、前端 2 例、IntegrationTests 需 `OPENAI__Key`）+ 前端 tsc/vitest/vite build（本 feature 前端仅提示文案，如无 UI 变更则仅回归）。
8. 三道质量门全绿；`.quality-gate.json` 推进 `f37-queued-execution`（`cleared:true` + `codebaseOptimizer`）；质量报告 `docs/quality/f37-queued-execution-gate.md`。
9. 无 UI 交互变更 → 不新增 BDD E2E（以 InMemory 队列 + enqueue 端点的 SpecFlow BDD 场景覆盖真 HTTP 契约）。

## 5. 决策（已锁定，2026-09-01 用户拍板）

- **D1 v1 范围 = B（含 RabbitMQ）**：三后端全做——`InMemory`（默认，进程内 Channel 有界 256）/ `RedisStream`（消费组 + XAUTOCLAIM 空闲回收）/ `RabbitMQ`（durable 队列 + BasicGet pull + prefetch=1 + unacked 断线重投）。`IExecutionQueue` 统一抽象，按 `DurableExecution:QueueBackend` 条件注册。
- **D2 端点契约 = B（run 透明入队+等待）**：`QueueEnabled=true` 时既有 `POST /workflows/{id}/run` 与 `run-existing` 透明改为「入队 → 轮询等待终态（上限 `QueueWaitTimeoutSeconds`，默认 110s，低于前端 axios 120s）→ 返回与今日同构的 Workflow 聚合」；等待超时返回 **202 `{queued:true, workflowId, state}`**（显式不假成功），前端 runWorkflow 调用点识别 queued 标记并提示「已入队执行，进度见 SSE」。`QueueEnabled=false`（默认）请求内直跑路径零变化。
- **D3 接管窗口 = A（复用 5min 租约）**：消息重投/idle 阈值 = `DurableExecution:LeaseTtlMinutes`（现状 5min，F31 语义不动）；记录与 backlog 原文「30s」的偏差（30s 需同时缩租约、会改 F30 崩溃恢复窗口，未选）。
- **D4 评估门禁 = A（保持同步直跑）**：门禁天然阻塞语义要求同步，队列模式不改门禁路径（满足 backlog「队列模式下仍正常工作」= 直跑路径恒在）。

## 6. 风险与缓解

- 🔴 消息可靠性：至少一次投递 + 租约互斥 + 幂等消费（TryAcquireLease 天然幂等）+ dead-letter 兜底；不做 exactly-once。
- 🔴 触发链行为变更仅限 `QueueEnabled=true`（默认关），CI/BDD 基座（移除 hosted services + Cache:Provider=Memory）天然隔离。
- 🟡 Redis 不可用：启动探测 + 运行期异常降级告警；入队端点显式失败（不静默丢任务）。
- 🟡 长编排占住 worker：v1 单 worker 内顺序消费（`WorkerConcurrency` 预留参数但 v1 固定 1，避免半吊子并发）。
- 🟢 不动执行引擎、不动既有同步 run 契约、不动评估门禁。

## 7. 实现说明（与设计的偏差）

- backlog 原文的独立 `POST /workflows/{id}/enqueue` 端点按决策 D2=B 取消：既有 `POST`（创建即跑）与 `POST /{id}/run` 在 `QueueEnabled=true` 时透明「入队+等待」，200=等待窗口内终态聚合、202 `{queued:true, workflowId, state}`=超时未终态、503 ProblemDetails=入队被拒（队列满/后端不可用，工作流已落库未丢）。`QueueEnabled=false` 全链路与 F36 基线一致。
- 触发器投递失败按「匿名 Webhook 可用性优先」降级直跑（记 warning），非静默。
- 等待轮询使用 `GetByIdFreshAsync`（AsNoTracking）——见 §8-R2。
- 前端 `runWorkflow`/`runExistingWorkflow` 返回 `Workflow(Detail) | QueuedRunResponse` 联合类型，`isQueuedRunResponse` 类型守卫判别，queued 分支提示 `pages.workflows.queuedRun`（zh/en 对称）。

## 8. 审查修复记录（ddd-code-reviewer 对抗审查，2026-09-01）

| # | 级别 | 文件:行（修复前） | 问题 | 修复 |
|---|------|------------------|------|------|
| R1 | P0 | `Application/Workflows/Commands/ExecuteQueuedWorkflow/ExecuteQueuedWorkflowCommand.cs:75-87` | 人工作业入队前恒 Pending 落库；「Running→Duplicate 预检 + 终态 Reset 重跑」组合导致：①ack 失败/XAUTOCLAIM 重投的重复投递把**已完成工作流 Reset 二次执行**（双成本+审计错乱）；②崩溃接管场景（Running+过期租约重投）被预检误判 Duplicate 吞掉，违背验收 3 | 删除 Running 预检与 Reset 分支：Running 统一交 F30 租约仲裁（活租约→冲突异常→Duplicate；过期租约→获锁接管续跑）；终态/暂停消费 = 已执行的重投 → Duplicate 跳过。回归测试 3 条（含终态不重跑守卫、过期租约接管） |
| R2 | P1 | `Application/Workflows/QueuedRunSupport.cs:46` | 等待轮询用 `GetByIdAsync`（FindAsync 命中请求 scope 追踪器）恒返回 Pending 陈旧实例 → 队列模式 run **永远等满超时误返 202**（Api E2E 用「OK 或 202」宽松断言+20s 超时掩盖了此缺陷） | `IWorkflowRepository` 新增 `GetByIdFreshAsync`（AsNoTracking 读库，租户/工作空间过滤照常），轮询切换；`WorkflowRepository` 实现；测试 stub 同步。修复后 Api.Tests 队列 E2E 全 suite 5s 内返回 200 |
| R3 | P1 | `Infrastructure/Queues/ExecutionWorker.cs:71-72,116-129` | 失败处理无论重投/死信是否成功都 ack 原投递：**「DeadLetter 写入失败（Redis 吞异常返回）或重试入队被拒 + 原投递已 ack」→ 任务彻底丢失**，违背 §6「绝不静默丢任务」 | `IExecutionQueue.DeadLetterAsync` 改 `Task<bool>`（三实现回报落存成败）；worker ack 门控：仅 `Executed/NotFound/Duplicate` 或「重投 Enqueued / 死信 true」才 ack，否则保留未 ack 交 Redis PEL / Rabbit unacked 重投兜底；2 条新 worker 回归测试（死信失败不 ack、重投被拒不 ack） |
| R4 | P1 | `Infrastructure/Queues/RedisStreamExecutionQueue.cs:235-256` | `AbortOnConnectFail=false` 下 Redis 不可达时**每次读循环（~2s）新建 ConnectionMultiplexer 且不释放旧的**（旧 mux 后台重连线程/socket 无界泄漏）| 单例复用：仅 null 时建连（`_connectLock` 双检防并发建连），mux 自带后台重连；`DisposeAsync` 释放信号量 |
| R5 | P1 | `Infrastructure/Queues/RabbitMqExecutionQueue.cs:94,134` | ①receipt=裸 deliveryTag，**channel 重建后 tag 从 1 重新计数**——旧 receipt 在新 channel 上 ack = INVALID_DELIVERY 报错或误 ack 他人消息（丢任务）；②注释宣称 SemaphoreSlim 串行 channel，实际仅建连持锁，BasicPublish/Get/Ack 全部锁外裸用共享 channel | 全部 channel I/O 收进 `_gate`（`WithChannelAsync`）；receipt 编码 channel epoch，跨代 ack 拒绝并告警（broker 已重投原消息，幂等兜底）；旧句柄 `DisposeAsync` 释放；类注释如实化（BasicGet 拉模式不吃 prefetch） |
| R6 | P2 | `Infrastructure/Queues/RedisStreamExecutionQueue.cs:119,203-233` | 消费组丢失（stream 被删/清空）后 `_groupEnsured=true` 永不重建设组 → 读循环 NOGROUP 永久空转；`StreamReadGroupAsync` 返回 null（服务端 nil）时 NRE | NOGROUP 异常重置 `_groupEnsured` 自愈重建；`entries is { Length: > 0 }` 守卫 |
| R7 | P2 | `Infrastructure/Queues/InProcessExecutionQueue.cs:63-68` | 类注释承诺毒消息「记 error 日志（含完整载荷）」，实现为空方法体静默丢弃 | 注入 `ILogger`，DeadLetter 记含完整载荷的 error 日志（进程内无持久死信的如实记录）；InMemory 重启丢未消费作业为已文档化限制（验收 4/风险表已声明） |
| R8 | P3 | `Infrastructure/Queues/RedisStreamExecutionQueue.cs`（EnqueueAsync） | XADD `MAXLEN ~` 修剪上限 `100_000` 为未命名魔法数（硬编码审查项） | 提取为具名常量 `StreamTrimMaxLength` + 中文文档注释（毒积压护栏，非按部署可调项，不新增配置面），值不变 |
| R9 | P3 | `Application/Workflows/QueuedRunSupport.cs`（BuildJob） | `ExecutionJob.EnqueuedAt` 文档为「首次入队时间（UTC）」却从未赋值 → 恒 null（未接线的载荷字段） | `BuildJob` 构造时以 `EnqueuedAt: DateTime.UtcNow` 落时间戳，使序列化载荷自描述（死信诊断可读） |

**已声明的残余风险（非缺陷，设计接受）**：触发投递（webhook/schedule）的「执行完成但 ack 失败」重投会重跑一次（FromQueue 分支需支持终态重放语义，无法与「首次投递」区分，除非引入按 JobId 的去重存储——schema 级结构决策，超出 v1；§6 已锁定「至少一次 + 不做 exactly-once」，消费幂等由租约/终态跳过兜住人工路径）。执行时长超过 `LeaseTtlMinutes` 时 XAUTOCLAIM 会把在途投递判给他人 → Duplicate-ack 消费掉原 pending 消息，崩溃恢复退化为 WorkflowScheduler 租约恢复路径（30s 轮询，语义等价，时效略降）。

**验证**：`dotnet build AgentPlatform.sln` 0 警告 0 错误；`Application.Tests 268/268`、`Infrastructure.Tests 171 通过 + 8 跳过（docker 门控）`、`Api.Tests 37/37`、`ArchitectureTests 9/9` 全绿（`OPENAI_API_KEY=sk-test-placeholder --no-build`）。既有豁免（SpecFlow LLM 1 例、前端 vitest 2 例、IntegrationTests 需 OPENAI__Key）未触碰。

## 9. codebase-optimizer Round F37-01 修复记录（第三道质量门，scope=F37 diff）

| # | 级别 | 维度 | 文件:行（修复前） | 问题 | 修复 |
|---|------|------|------------------|------|------|
| R10 | P1 | 桩代码/工程化 | `.github/workflows/ci.yml:29-37` + `Infrastructure.Tests/Queues/ExecutionQueueTests.cs` | RabbitMQ SkippableFact 在 CI **恒静默跳过**：测试以 `amqp://localhost`（guest/guest）连接，而 RabbitMQ 默认 `loopback_users=[guest]` 仅允许容器内环回登录，GH Actions runner 经发布端口/网桥的连接非 loopback → ACCESS_REFUSED → probe 失败即 skip。CI 注释承诺「services 提供真实 broker 即跑投递闭环」对 RabbitMQ 落空 | service 改 `RABBITMQ_DEFAULT_USER/PASS=apci`（非 guest，不受环回限制）；job 级 env `ConnectionStrings__RabbitMQ` 提供同凭据；测试侧将该 env 注入 `DurableExecutionSettings.RabbitMqUrl`（本地不设置=回退 localhost guest，开发语义不变） |
| R11 | P2 | 测试/桩代码 | `Api.Tests/QueuedExecutionEndpointTests.cs` | 设计 §2 承诺该套件覆盖「满/不可用 503；超时 202」，实际仅 200/宽容 202——503 与**确定性** 202 契约在 HTTP 层留白未测；且既有类声明 `IClassFixture` 却直接 `new()` 工厂（双宿主、夹具空转） | 修复夹具注入（构造注入替代 `new()`）；新增 `ScriptedApiExecutionQueue` 假队列 + `QueueRejectingApiContractTestFactory`/`QueueStalledApiContractTestFactory`（`ConfigureTestServices` 顶替注册）+ 2 例：拒投→503（并断言工作流已落库 Pending、作业确达队列接缝）、停摆→1s 窗口超时→202 `{queued:true,workflowId,state}` |
| R12 | P2 | 正确性/契约 | `Api/Controllers/WorkflowsController.cs`（QueueResult） | 503 拒投时工作流已落库（§7「已落库未丢」）但响应体无 workflowId → 调用方拿到错误却无引用，只能翻页找孤儿 Pending 工作流 | ProblemDetails `Extensions` 回带 `workflowId`（RFC 7807 顶层成员），R11 新测试锁定 |
| R13 | P2 | 桩代码（契约字段留白） | `Application/Abstractions/IExecutionQueue.cs:26` 等 | `ExecutionJob.RequestingUserId` 文档承诺「审计归属」却**零生产零消费**（BuildJob 调用方从不传、worker 审计从不读） | 全链接线：run/run-existing 命令新增可选 `RequestingUserId`，控制器从 `ClaimTypes.NameIdentifier` 解析注入，`ExecuteQueuedWorkflowCommandHandler` 审计 details 携带发起用户；§8 清单同步澄清 `ExecuteQueuedWorkflowCommand` 标 `ICommand`（审计经 UnitOfWorkBehavior flush）与 run 类命令保持 `IRequest` 的例外语义 |
| R14 | P2 | 测试 | `Infrastructure.Tests/Queues/ExecutionQueueTests.cs` | 验收 4「dead-letter 真落盘」在 broker 级无断言（仅 ScriptedQueue 假死信） | Redis/Rabbit 两个 SkippableFact 增补 `Assert.True(await DeadLetterAsync(...))`（返回值仅在 XADD/发布确认实际成功时为 true） |
| R15 | P3 | 正确性 | `Infrastructure/Queues/RabbitMqExecutionQueue.cs`（ctor） | `RabbitMqUrl` 文档「空则回退」，实现 `??` 仅对 null 回退——配空串进 `new Uri("")` 永久失败 | 解析链改为逐层 `IsNullOrWhiteSpace` 视同未配置 |
| R16 | P3 | 代码质量 | `Application/WorkflowTriggers/TriggerWorkflowCommandHandler.cs`（F37 块）、`Web/src/pages/WorkflowsPage.tsx:37` | F37 新代码全限定名与同文件既有风格不一致（同文件直跑路径用裸 `OrchestrationPreset`，队列块却写 `AgentPlatform.Application.Abstractions.…`）；前端 import 缺分号 | 统一简化为裸类型名（补 using）；补分号 |

**Waiver（记录不修）**：① Redis 级 XAUTOCLAIM 崩溃接管集成用例——接管窗口=租约 TTL（5min）CI 不可行，接管仲裁语义已由 F30 租约测试 + `Execute_ExpiredLease_*` 消费者单测覆盖，风险=后端 claim API 误用（低）；② `InProcessExecutionQueue.DeadLetterAsync` 恒返回 true 但无持久死信通道——进程内后端「重启丢作业」为 §6/验收 4 已文档化限制（R7 语境），worker 侧 ack 本就无意义；③ Redis `CompleteAsync` 断连时静默不 ack——语义安全（PEL 重投兜底），日志已由读循环告警覆盖。

**验证**：见 `.codebase-optimizer/rounds/round-f37-01-report.md` 备注。

## Quality Gate Checklist

> ddd-phase-quality-gate（Mode 3）嵌入式清单。逐模块推进：完成一个模块 → 编译 0 警告 → 测试全绿 → DI 审计 → 分层审计 → 下一模块。

### 1. Pre-flight Version Audit
- [x] 引入包版本锁定：`StackExchange.Redis 2.8.0`（F30 已引，复用）、`RabbitMQ.Client 7.x`（`Infrastructure.csproj` 新增）——v7 异步 API（`BasicPublishAsync`/`BasicGetAsync`/`QueueDeclareAsync`）签名以安装包为准，非训练记忆
- [x] Redis Stream API 核对：`StreamAddAsync(MAXLEN ~)` / `StreamReadGroupAsync(">")` / `StreamAcknowledgeAsync` / `StreamAutoClaimAsync`（XCLAIM 需 Redis ≥6.2，低版本 `TryClaimIdleAsync` 捕获降级）
- [x] 基线 `dotnet build` 在改码前 0/0

### 2. BDD / 测试基座 First
- [x] `ApiContractTestFactory` 派生 `QueueModeApiContractTestFactory`（protected ctor + 派生类，规避 xUnit 夹具唯一公共构造函数限制）；`_queueMode` 默认 false → 既有 35+ Api 例走直跑路径零变化
- [x] `QueuedExecutionEndpointTests`：InMemory 队列真 HTTP 契约（enqueue→worker→终态 200；满/不可用 503；超时 202）
- [x] `ExecutionQueueTests`：InProcess FIFO/有界拒投/空读；RedisStream/RabbitMQ 走 `SkippableFact`（`ProbeAsync && Enqueue==Enqueued` 门控，CI ubuntu+docker 提供真实 broker）
- [x] `ExecutionWorkerTests`：R3 消息可靠性回归（死信失败不 ack、重投被拒不 ack、成功/Duplicate/NotFound ack）
- [x] 边界用例：队列满、后端不可用、Attempt≥MaxAttempts 落 dead-letter、跨租户拒执行

### 3. DDD Layer Rules
- [x] `IExecutionQueue`/`ExecutionJob`/`QueueDelivery`/`EnqueueResult` 定义于 `Application.Abstractions`
- [x] `RedisStreamExecutionQueue`/`RabbitMqExecutionQueue`/`InProcessExecutionQueue`/`ExecutionWorker` 实现于 `Infrastructure.Queues`
- [x] DI 注册在 `Infrastructure/DependencyInjection.cs`；`IWorkflowRepository` 既有接口扩展 `GetByIdFreshAsync`（`Domain.Repositories`）+ `WorkflowRepository` 实现
- [x] Application 零 `using AgentPlatform.Infrastructure`（grep 0 命中）；Domain 无新增外部包

### 4. DI Registration Completeness
- [x] `IExecutionQueue` = 单例，工厂按 `DurableExecution:QueueBackend` 三选一（默认 `_`→InProcess，未识别值回退 InMemory=fail-safe）
- [x] 生命周期正确性：`ActivatorUtilities.CreateInstance` 于单例工厂内返回 → 容器登记并负责 Dispose（Redis/Rabbit `IAsyncDisposable.DisposeAsync` 释放自建连接/信号量；InProcess `IDisposable`）
- [x] `ExecuteQueuedWorkflowCommandHandler`/改造后的 Run/RunExisting/Trigger handler 经 MediatR `RegisterServicesFromAssembly(Application)` 自动注册
- [x] `ExecutionWorker` 条件注册：仅 `QueueEnabled=true`；测试基座另移除全部 `IHostedService` 双保险

### 5. Configuration-First
- [x] `DurableExecutionSettings` 新增 QueueEnabled/QueueBackend/QueueWaitTimeoutSeconds/QueuePollIntervalSeconds/QueueMaxAttempts/QueueCapacity/RedisStreamKey/RedisDeadLetterKey/RabbitQueueName/RabbitDeadLetterQueueName/RabbitMqUrl/WorkerIdleDelayMilliseconds
- [x] `appsettings.json` `DurableExecution` 节键与 DI 默认、settings 默认三处一致；`QueueEnabled=false` 默认
- [x] 连接串走配置（`ConnectionStrings:Redis`/`Redis:ConnectionString`、`RabbitMqUrl`/`ConnectionStrings:RabbitMQ`），无写死密钥；消费组名常量化 `ap-workers`；键名可配置

### 6. EF Core Mapping Sync
- [x] 无新增聚合/VO → 无新 `IEntityTypeConfiguration`、无新迁移
- [x] `GetByIdFreshAsync` = `AsNoTracking().FirstOrDefaultAsync`，租户/工作空间全局过滤器照常生效（轮询读库语义正确）

### 7. Concurrency & Lifecycle
- [x] InMemory `Channel<T>` 线程安全（有界 256，`TryWrite` 非阻塞满即显式拒投）
- [x] Redis 单例：`_connection` volatile + `_connectLock` 双检（审查 R4）；`_groupEnsured` 于 `_groupInitLock` 双检 + NOGROUP 自愈重置（审查 R6）；v1 单 worker 顺序消费（`AddHostedService` 一实例），无同进程并发读者
- [x] Rabbit 单例：全部 channel I/O 收进 `_gate`，`_epoch` `Interlocked`+`Volatile` 界定 deliveryTag 有效性（审查 R5）
- [x] SemaphoreSlim 释放路径：`_groupInitLock`/`_connectLock`/`_gate` 均 `finally` 释放并在 `DisposeAsync` 释放；acquire/release 对称
- [x] 无 grow-only 单例集合；停机/取消/异常退出路径不 ack（持久后端重投语义接管）
- [x] 幂等消费：终态/暂停 + Running 活租约→Duplicate 跳过（审查 R1），不复位重跑

### 8. Cross-Cutting Infrastructure
- [x] 控制器仅注入 `IMediator`+`ITenantProvider`（未破 MediatR 规约）；run/run-existing/trigger 保持 `IRequest<T>`（编排器自管持久化，勿标 `ICommand<T>` 防双 SaveChanges）；worker 侧 `ExecuteQueuedWorkflowCommand` 例外标 `ICommand<QueuedRunOutcome>`——worker scope 无显式 SaveChanges，审计持久化正依赖 UnitOfWorkBehavior 收尾提交（optimizer R13 澄清原表述）
- [x] 所有新 async 方法带 `CancellationToken`；实现类 `internal sealed`；`WorkflowRunResult`/`ExistingWorkflowRunResult`/`ExecutionJob` 公共记录带中文 XML 文档
- [x] Api 三态契约：200 完成聚合 / 202 `{queued,workflowId,state}` / 503 ProblemDetails（拒投显式失败，绝不静默丢任务）
- [x] 契约破坏性改造全量核对：`run`/`run-existing` 结果类型变更仅 `WorkflowsController` + 测试构造，benchmark/Program/SpecFlow（走 HTTP，默认直跑态契约不变）无遗漏调用方（全仓 grep 验证）
- [x] 前端：`isQueuedRunResponse` 类型守卫判别联合返回；`queuedRun` i18n zh/en 对称；无新增 effect/hook 依赖、无 AbortSignal 回归；tsc/vitest/vite build 仅回归
- [x] CI/docker-compose 增 redis + rabbitmq（healthcheck），门控集成测试可跑；`dotnet build` 0/0、`dotnet test` 全绿

### Incremental Gate Sequence（模块推进序）
1. Application 队列抽象（`IExecutionQueue`/`ExecutionJob`/`WorkflowRunResult`/`DurableExecutionSettings`）→ build 0/0 → Application.Tests 绿
2. run 命令结果类型改造（Run/RunExisting/Trigger handler + `QueuedRunSupport`/`ExecuteQueuedWorkflowCommand`/`GetByIdFreshAsync`）→ build → Application.Tests + DI 审计
3. Infrastructure 三后端 + `ExecutionWorker` → build → Infrastructure.Tests（含 docker 门控）→ DI 生命周期审计
4. Api 三态契约（`WorkflowsController`/`appsettings`/`Program` 无改）→ build → Api.Tests
5. 前端 queued 分支（`api.ts`/`types`/pages/locales）→ tsc + vitest + vite build
6. 配置/CI/compose 对齐 → 全量回归

### Final Regression
- [x] 全量 `dotnet build AgentPlatform.sln` 0 警告 0 错误
- [x] 全量 `dotnet test`（豁免：SpecFlow LLM 1 例、前端 vitest 2 例、IntegrationTests 需 `OPENAI__Key`）
- [ ] `QueueEnabled=true`+InMemory 端到端人工 journeys（见 Api E2E）
- [x] 无新 P0/P1 审计发现；§7/§8 修复记录与本门审计一致

