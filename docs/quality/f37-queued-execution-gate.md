# F37 · 队列化执行与水平扩展质量门报告

> 日期：2026-09-02 · 分支 `feat/f37-queued-execution`（基于 `feat/f36-agent-context-isolation`）· feature-builder 全栈流水线
> 设计文档：`features/f37-queued-execution.md`（§5 决策 D1–D4 用户锁定 2026-09-01；§8 审查修复记录）

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer | **PASS**（0 open） | 1×P0 + 4×P1 + 3×P2 + P3 全部修复 |
| ddd-phase-quality-gate | **PASS**（P0=P1=P2=0；P3×2 修，0 waiver） | checklist 嵌入设计文档 |
| codebase-optimizer | **PASS**（Round F37-01，0 open） | P3×1 修（未知 QueueBackend 告警），2×P3 waiver |

## ddd-code-reviewer 修复记录（关键项）

| 严重度 | 文件 | 问题 | 修复 |
|---|---|---|---|
| P0 | ExecuteQueuedWorkflowCommand | 至少一次投递下「重复投递 + 工作流终态」会 `Reset` 二次执行已完成工作流 | 终态/暂停→Duplicate 跳过，绝不 Reset 重跑；Running 交 F30 租约仲裁（活租约→Duplicate、过期→接管续跑）；+3 回归测试 |
| P1 | QueuedRunSupport | 轮询 `GetByIdAsync`(=FindAsync) 命中请求 scope 追踪器恒返回陈旧 Pending → 队列 run 必等满超时误返 202 | 新增 `IWorkflowRepository.GetByIdFreshAsync`(AsNoTracking)，轮询改用 |
| P1 | ExecutionWorker | dead-letter/重投吞异常仍 ack 原投递 → 任务彻底丢失 | `DeadLetterAsync`→`Task<bool>` 三实现回报成败；仅接管成功才 ack，否则留待后端重投 |
| P1 | RedisStreamExecutionQueue | `IsConnected=false` 时每读循环新建 multiplexer 不释放旧 → 无界泄漏 | 单例双检复用 + `AbortOnConnectFail=false` 自带重连 + Dispose |
| P1 | RabbitMqExecutionQueue | deliveryTag 跨 channel 失效误 ack / I/O 未真正串行化 | receipt 编码 channel epoch，跨代拒 ack；全部 Basic* 收进 `SemaphoreSlim` gate |
| P2 | RedisStream | 消费组丢失永久空转 / StreamReadGroup nil NRE | NOGROUP 自愈重建 + 空数组守卫 |
| P2 | InProcessExecutionQueue | 注释承诺记 error 实为静默丢弃 dead-letter | 注入 ILogger 如实记 error 并返回 bool |

## 结构门 / optimizer 复核

DI 注册完备（IExecutionQueue 单例三后端条件选择 + 容器接管 IAsyncDisposable/IDisposable；ExecutionWorker 恒注册运行时门控；GetByIdFreshAsync）；无桩代码（三后端均真实实现，SkippableFact 在 CI redis/rabbitmq services 下真跑投递闭环）；连接串走配置无硬编码；载荷含 Tenant/Workspace 且消费侧跨租户拒执行；worker ack/重投/死信接管决策严密。Waiver（optimizer 2×P3）：`ExecutionJob.RequestingUserId` v1 未从 controller 透传（审计归属可后补，不影响执行）；InMemory 无持久 dead-letter（单实例回退设计接受，多实例生产用 Redis/Rabbit）。

## 决策校正记录（相对 backlog 原文）

- **接管窗口 5min 非 30s**（D3=A）：现网 `LeaseTtlMinutes=5`，缩至 30s 会改 F30 崩溃恢复窗口，未选。
- 复用既有 `IDistributedLockProvider`（Redis 实现本即 SET NX PX），未新建 `DistributedLeaseProvider`；队列投递在 run 处理器内透明完成，未新增公开 `EnqueueWorkflowRunCommand`；后端为注册期选定，Redis/Rabbit 不可用 run 端点显式 503（不运行期静默切 InMemory，避免多实例脑裂）。

## 验证

- 后端：`dotnet build AgentPlatform.sln` 0 警告 0 错误；`dotnet list package --vulnerable` 无（RabbitMQ.Client 7.1.2 / StackExchange.Redis 2.8.0）。测试：Application **268/268**、Infrastructure **171+8 跳**（broker 门控）、Api **37/37**、Architecture **9/9**、Integration **5/5**（需 `OPENAI__Key`）、SpecFlow **115/116**（唯一失败 = master 既有 LLM 用例，已验证同样失败）。
- 新增测试：Application 队列 15（入队等待三态 / 幂等 Duplicate / 跨租户拒跑 / OCE 穿透 / 触发 FromQueue）、Infrastructure queue+worker 9（InMemory FIFO/有界拒投/空读、Redis/Rabbit 投递闭环 SkippableFact、worker ack/重投/死信）、Api 队列模式 E2E 2（真 HTTP→队列→worker→租约→终态）。
- 前端：`tsc --noEmit` 0 error；vitest 42 过/2 既有豁免；`vite build` 通过。
- 模型一致性：run 端点 `200 聚合 / 202 {queued,workflowId,state} / 503 ProblemDetails`；前端 `Workflow | QueuedRunResponse` union + `isQueuedRunResponse` 守卫对齐。

## 已知残留（非阻断）

1. Redis/Rabbit 真 broker 投递闭环仅 CI services 覆盖（本地无 broker 跳过）。
2. InMemory 后端进程重启丢未 ack 作业（单实例回退定位）。
3. 触发投递重跑会重放载荷（at-least-once，明确不做 exactly-once，JobId 去重需 schema 级决策）。
