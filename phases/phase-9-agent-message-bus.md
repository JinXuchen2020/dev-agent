# 阶段九：Agent 消息总线 + 多 Agent 协作

> 学习目标：引入 agent 间消息总线，把 `NegotiationOrchestrator` 从"单 LLM 步骤选择"升级为"真正并行 agent 协作"（handoff / 协商 / 收敛）。本阶段让 dev-agent 拥有"agent 社会原语"。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.2 / §3 层2 / §4 Phase 9 / §5 D2 / §6 / §7）、`./phase-8-agent-runtime.md`（前置：agent 运行时实体）、`../docs/quality/*`（质量门）。

## 学习目标

- [ ] **消息驱动架构**：`Channel<T>`（in-process）作为起步传输，理解生产者 / 消费者 / 消息类型设计
- [ ] **多 Agent 并行推理**：N 个 agent 实例并发推理并通过消息收敛，而非顺序发言退化
- [ ] **Handoff / 协商模式**：agent 间任务移交、协商终止条件（对齐 phase-6 提到的 negotiation 预设真实 selection/termination，禁止顺序退化）
- [ ] **消息持久化 + 幂等**：消息至少一次投递 + 幂等消费，防重复处理
- [ ] **活锁 / 死锁防治**：消息风暴、活锁的检测与熔断

## 前置依赖

- [ ] 阶段八 Agent 运行时实体化已完成并提交（agent 是一等运行时实体、有独立上下文，本阶段才能让其"发消息"）
- [ ] 已锁定蓝图决策 **D2**：Phase 9 起步用 in-process `Channel<T>`，可选 broker（如 Dapr / RabbitMQ）留待 Phase 11
- [ ] 已确认 `NegotiationOrchestrator` 当前实现为"单 LLM 步骤选择"（蓝图 §0 偏差 / phase-6 提到须用真实 selection/termination），避免新写一层又退化

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [ ] 重核实 `NegotiationOrchestrator` 当前协作逻辑——确认其为**单 LLM 步骤选择**，尚无 agent 间消息传递、独立上下文或并行推理（蓝图 §3 层2）。
- [ ] 重核实 agent 运行时现状（阶段八产出）：确认 agent 已具备独立上下文窗口与 Blackboard 分区，可作为消息总线的独立端点。

### 实现任务

- [ ] **AgentMessageBus（in-process 起步）**：基于 `System.Threading.Channels.Channel<T>` 实现 `IAgentMessageBus`；定义消息类型（`AgentMessage`：SenderId / ReceiverId / Type / Payload / CorrelationId / Timestamp）+ 订阅 / 发布 API。🔍 强制 `ddd-phase-quality-gate`：核对 DI 作用域 / 密封 / 空守卫 / 接口非空壳。
- [ ] **并行 Agent 协作**：`NegotiationOrchestrator` 升级为真正并行——N 个 agent 实例并发推理，通过总线发消息、handoff、协商，按终止条件收敛（对齐 phase-6 的 negotiation 真实 selection/termination，禁止顺序发言退化）。🔍 强制 `ddd-code-reviewer`：核对多 agent **真实并行**推理（非单 LLM 串行伪装）、收敛条件真实生效、无顺序退化。
- [ ] **消息持久化 + 幂等**：消息落 `AgentMessageLog`（ITenantScoped）；消费端按 `CorrelationId` + 去重表幂等；至少一次投递语义。🔍 强制 `ddd-code-reviewer`：核对消息不丢失、幂等消费真实生效（重投不重复处理）。
- [ ] **活锁 / 风暴防治**：消息环路检测、单轮消息数上限、停滞超时（无进展则终止并上报）；可观测消息流（trace 埋点）。🔍 强制 `ddd-code-reviewer`：核对消息风暴 / 活锁有真实熔断、trace 能回放消息流。

## 验收标准

1. 一个协作场景里 ≥2 个 agent 经消息总线**并行**推理并收敛出结果（非顺序发言）。
2. handoff 模式真实生效：任务在 agent 间移交且上下文随移交传递。
3. 消息持久化 + 幂等：进程重启后未消费消息可重投、已消费消息不重复处理。
4. 构造消息环路 / 风暴时，系统熔断（超时终止 + 告警），无活锁挂死。
5. 消息流可在 trace / 日志中回放，便于排查。

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

- **开始日期**：
- **完成日期**：
- **完成度**：█░░░░░░░░░ 0%

## 回顾（完成后填写）

### 做得好的

### 下次改进

### 对蓝图文档的反馈
