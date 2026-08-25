# 阶段九：Agent 消息总线 + 多 Agent 协作

> 学习目标：引入 agent 间消息总线，把 `NegotiationOrchestrator` 从"单 LLM 步骤选择"升级为"真正并行 agent 协作"（handoff / 协商 / 收敛）。本阶段让 dev-agent 拥有"agent 社会原语"。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.2 / §3 层2 / §4 Phase 9 / §5 D2 / §6 / §7）、`./phase-8-agent-runtime.md`（前置：agent 运行时实体）、`../docs/quality/*`（质量门）。

## 学习目标

- [x] **消息驱动架构**：`Channel<T>`（in-process）作为起步传输，理解生产者 / 消费者 / 消息类型设计
- [x] **多 Agent 并行推理**：N 个 agent 实例并发推理并通过消息收敛，而非顺序发言退化
- [x] **Handoff / 协商模式**：agent 间任务移交、协商终止条件（对齐 phase-6 提到的 negotiation 真实 selection/termination，禁止顺序退化）
- [x] **消息持久化 + 幂等**：消息至少一次投递 + 幂等消费，防重复处理
- [x] **活锁 / 死锁防治**：消息风暴、活锁的检测与熔断

## 前置依赖

- [x] 阶段八 Agent 运行时实体化已完成并提交（agent 是一等运行时实体、有独立上下文，本阶段才能让其"发消息"）
- [x] 已锁定蓝图决策 **D2**：Phase 9 起步用 in-process `Channel<T>`，可选 broker（如 Dapr / RabbitMQ）留待 Phase 11
- [x] 已确认 `NegotiationOrchestrator` 当前实现为"单 LLM 步骤选择"（现状核实完成，见 features/f32-agent-message-bus.md §1）

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [x] 重核实 `NegotiationOrchestrator` 当前协作逻辑——确认为单步规则选择 + 串行执行
- [x] 重核实 agent 运行时现状（阶段八产出）：F31 后 executor 按 AssignedAgentId 加载 SystemPrompt/ModelEndpoint

### 实现任务

- [x] **AgentMessageBus（in-process 起步）**：基于 `System.Threading.Channels.Channel<T>` 实现 `IAgentMessageBus`；AgentMessage 契约 + 发布/排空 API。🔍 强制 `ddd-phase-quality-gate`
- [x] **并行 Agent 协作**：NegotiationOrchestrator 双模式升级——绑定 agent 时 Task.WhenAll 真并行提案 + critic 收敛；无绑定 agent 诚实降级串行循环。🔍 强制 `ddd-code-reviewer`
- [x] **消息持久化 + 幂等**：AgentMessageLog（ITenantScoped）写穿落库；TryMarkConsumed 条件更新幂等；未消费重投。🔍 强制 `ddd-code-reviewer`
- [x] **活锁 / 风暴防治**：单轮消息预算 / 停滞超时 / 环路指纹三防线熔断 Paused+告警；CorrelationId+落库支持 trace 回放。🔍 强制 `ddd-code-reviewer`

## 验收标准

1. ✅ 一个协作场景里 ≥2 个 agent 经消息总线**并行**推理并收敛出结果——双 RouteAsync 时间窗重叠实证
2. ✅ handoff 模式真实生效：critic 拒绝 → Handoff 定向移交其他绑定 agent，反馈上下文随 payload 传递
3. ✅ 消息持久化 + 幂等：写穿落库 + TryMarkConsumed 条件更新（重投不重复处理）；跨进程重投经 RepublishUnconsumed 进入新一轮
4. ✅ 构造消息环路 / 风暴时熔断（预算超限/停滞超时 → Paused + 告警日志），无活锁挂死
5. ✅ 消息流可在 trace / 日志中回放（CorrelationId + AgentMessageLog 全量留痕）

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。AgentMessageBus / NegotiationOrchestrator 并行协作属"叙事性能力"，合入前强制 `ddd-code-reviewer`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性能力"的模块（AgentMessageBus / NegotiationOrchestrator 并行协作 / 消息幂等——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §3/§4 Phase 9、依赖是否真实使用、并行是否真实（非串行伪装）、是否存在"顺序退化"漂移 |
| 纯基础设施 / 结构卫生模块（AgentMessageLog 仓储 / DI / EF 映射 / Channel 配置） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 蓝图 §3 / §4 Phase 9 / 阶段九验收标准"）。缺此项即视为未通过。

### Phase 9 强制范围（高风险叙事性模块）

- **AgentMessageBus / 并行协作**：核对 §3 层2 / §4 Phase 9；重点验证多 agent 真实并行（非单 LLM 串行）、收敛 / handoff 真实生效、无顺序退化。
- **消息持久化 + 幂等 + 风暴防治**：核对 §4 Phase 9 / §6；重点验证消息不丢、幂等消费、活锁熔断真实生效。

> 规划提示：阶段九让平台拥有"agent 社会原语"，本 §0 要求在此阶段启动前即明确——上述模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-08-25
- **完成日期**：2026-08-25（v1；分布式 broker 与 Negotiation durable 挂起留 Phase 11）
- **完成度**：██████████ 100%

## 回顾（完成后填写）

### 做得好的
- 双模式门禁设计：协作与串行路径契约清晰，旧测试零改动全绿
- 并行段线程安全由构造保证——纯 RouteAsync I/O 并发，EF/事件严格单线程
- 时间窗重叠测试用 Task.Run 包裹桩延迟，实证真并行而非串行伪装

### 下次改进
- NSubstitute 返回已完成 Task 会让 WhenAll 蜕化串行——写并发断言时必须用真实异步包装，已记入经验
- `nvarchar(max)` 列类型陷阱应写入 docs/learning 排障手册（SQLite DDL 不接受）

### 对蓝图文档的反馈
- §3 层2「禁止顺序退化」的表述促成了双模式门禁设计，避免为过测试而伪造并行
