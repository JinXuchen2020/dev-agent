# Blueprint Architecture Review — 自研 Agent 编排平台 (2026-07-16)

## Verdict: DESIGN NEEDS WORK → 复审后 **DESIGN READY**（附录 C 已重写，见文末复审结论）

## Summary

工程底座（DDD 分层 / DI / 依赖倒置 / 可观测性 / 安全）是教科书级的扎实，但**多 Agent 编排的范式本身是"线性瀑布"**：两层调度（状态机 + 群聊管理器）都是串行的，平台上不存在真正的 peer 协商、critique/reflection 循环，也没有上下文伸缩策略。这些是**蓝图自身的设计选择**，不是实现偏离——所以 `ddd-code-reviewer`（实现保真）和 `ddd-phase-quality-gate`（DDD 结构）都抓不到，必须由本评审在动手前拦住。共 4 个 P1、5 个 P2，无 P0 阻断项；实现可推进，但 P1 应在 Phase 2 内至少补上设计。

## Findings

| # | Dimension | Severity | Blueprint Ref | Issue | Recommendation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| F1 | Topology | **P1** | 附录 C.2 / C.5 | 编排是纯顺序管线，且连"群聊"也用 `SequentialGroupChatManager`（顺序发言）——两层都串行，无 peer 协商 | 提供协商拓扑（真群聊 + 选择/终止策略，或 critic 循环）作为可选路径，线性仅作 fast path |
| F2 | Critique/Reflection | **P1** | 附录 C.6 | "重试"=退回到上一步重跑，是 re-execution 不是 critique；无 peer 评审环 | 加 critique 步（如架构师审开发代码返回精准反馈，仅路由回开发），而非整轮重跑 |
| F3 | Context Scaling | **P1** | 附录 C.5 / C.3 | 群聊模式把 6 Agent 全部历史持续追加，无压缩/检索/窗口上限 → token 线性爆炸 | 定义上下文策略：共享工作区/黑板、逐步摘要压缩、检索增强上下文、单 Agent 接收量封顶 |
| F4 | Context Consistency | **P1** | 附录 C.3 vs C.5 | 两套不一致上下文模型：状态机只传上一步 `OutputPayload`，群聊传全量历史 | 统一一个上下文契约（如共享 workflow context 对象），调度层与协商层都消费它 |
| F5 | RAG Grounding | P2 | §一 vs 附录 C | RAG 列为平台能力，但协作章节从不把召回知识注入 Agent 上下文 | 定义检索如何进入上下文（生成前检索 / 知识步注入） |
| F6 | HITL Routing | P2 | 附录 C.5 / C.6 | HITL 仅列为 AutoGen"可选"能力；回滚发 `HumanInterventionRequired` 事件但无检查点/路由/恢复设计 | 明确哪些步可暂停、人看到/能改什么、如何从干预点恢复 |
| F7 | Role Fidelity | P2 | 附录 C.1 / C.5 | 6 角色仅靠 System Prompt 区分，仅 Developer 有独立工具集（codeTools）——"一个模型戴 6 顶帽子" | 按角色区分工具集/知识/权限，使每个 Agent 带来独特能力 |
| F8 | Quality Closure | P2 | 附录 C.6 / C.7 | "测试工程师"产出测试**报告**而非跑测试；流水线出口=产出文档，非"测试通过" | 区分"文档生成步"与"执行/验证步"，标注哪些质量门是声明 vs 已证实 |
| F9 | Recovery Over-promise | P2 | 附录 C.7 | "任何一步崩溃都能恢复 / 全量持久化"是绝对承诺，但蓝图未规定逐步持久化，承诺无证据 | 改为"每步结果落库、崩溃从中断步恢复"并附 kill+restart 集成测试证明；回滚目标需精准（C.6 已设计，保留） |

### Detail

#### F1 — 编排拓扑纯线性（P1，附录 C.2 / C.5）
- **Observation**：附录 C.2 明确定义"顺序管线（Pipeline）"为阶段二主模式，核心原则"每个 Agent 的输出是下一个 Agent 的输入"；附录 C.5 的群聊示例 `new GroupChat(..., groupChatManager: new SequentialGroupChatManager())`——发言策略仍是**顺序**，并非真正的辩论/协商。
- **Why it matters**：两层调度都是串行，意味着 Agent 之间永远无法互相挑战、纠错、补全。这正是"名为 multi-agent、实为瀑布 SDLC"的典型反模式，丧失了多 Agent 相对单 Agent 的核心增量价值。
- **Recommendation**：保留线性管线作 fast path，但增设协商拓扑——群聊采用真实选择/终止策略，或加入 critic 循环（评审者审作者产物后精准返修）。

#### F2 — 无 peer critique / reflection 循环（P1，附录 C.6）
- **Observation**：C.6 重试语义"退回到上一步，让 Agent 修复后重试"是**重跑上一步**，不是 critique；全文档无"某角色评审另一角色产物并返回精准反馈"的机制。
- **Why it matters**：re-execution ≠ critique。没有评审环，架构缺陷会在开发后才被发现，且只能整轮重跑，成本高、定位粗。
- **Recommendation**：加入 critique 步（如架构师评审开发代码返回 diff 级反馈、测试员返回精准缺陷清单仅路由回开发），范围化返修而非全流水线重启。

#### F3 — 上下文无伸缩策略（P1，附录 C.5 / C.3）
- **Observation**：C.5 叙事中群聊把 6 Agent 全部历史持续追加进 `conversationHistory`；全文无摘要/窗口上限/检索机制。C.3 状态机靠 `previousResult?.OutputPayload` 仅传上一步输出——两套都无压缩。
- **Why it matters**：历史随轮数线性膨胀，token 成本不可控、长流水线必触发截断、质量随步数退化。
- **Recommendation**：定义上下文策略——共享工作区/黑板、逐步摘要压缩、检索增强上下文、对单 Agent 接收量封顶。

#### F4 — 上下文模型不自洽（P1，附录 C.3 vs C.5）
- **Observation**：状态机只传上一步 `OutputPayload`（C.3），群聊传全量历史（C.5）；且平台同时存在"自研状态机"与"AutoGen.NET 编排层"两套引擎、各自上下文语义不同。
- **Why it matters**：同一平台两种上下文契约，行为在两引擎间发散，难以推理、测试、调试；后期合并或切换时会爆雷。
- **Recommendation**：确立唯一上下文契约（如共享 `WorkflowContext` 对象），调度层与协商层统一消费。

#### F5 — RAG 未接地进 Agent 上下文（P2，§一 vs 附录 C）
- **Observation**：§一将 RAG 列为平台能力（文档入库与召回），但附录 C 全篇协作机制从不把召回知识注入 Agent 上下文；Agent 只消费传递来的 JSON。
- **Why it matters**：RAG 能力被孤岛化，多 Agent 协作拿不到知识检索增益。
- **Recommendation**：定义检索如何进入上下文（生成前检索 / 知识步输出注入）。

#### F6 — HITL 只声明未设计（P2，附录 C.5 / C.6）
- **Observation**：C.5 把 HITL 列为 AutoGen"可选"能力；C.6 回滚时 `Publish(new HumanInterventionRequired(...))`，但无检查点规范、无人工可看/可改内容、无恢复流程。
- **Why it matters**：能力声明无法落地为可控的人工介入点，出事时人不知道在哪接、能改什么。
- **Recommendation**：明确可暂停的步骤、人工视图与可编辑项、从中断点恢复的机制。

#### F7 — 角色保真度低（P2，附录 C.1 / C.5）
- **Observation**：C.1 的 6 角色仅靠 System Prompt 区分，C.5 仅 Developer 绑定 `codeTools`，其余角色工具/知识/权限无差异。
- **Why it matters**："团队"实为同一模型戴 6 顶帽子，多 Agent 价值受限。
- **Recommendation**：按角色区分工具集/知识源/权限，使每个 Agent 具备独特能力。

#### F8 — 质量闭环是文档级（P2，附录 C.6 / C.7）
- **Observation**：C.6/C.7 中"测试工程师"产出测试**报告/缺陷报告**，流水线出口是"产出…测试报告"而非"测试通过"。真实执行留到 Phase 4 沙箱。
- **Why it matters**：核心流水线质量门是声明式（生成报告）而非验证式（跑通），容易"纸面通过"。
- **Recommendation**：区分文档生成步与执行验证步，标注哪些门是声明 vs 已证实。

#### F9 — 恢复能力过度承诺（P2，附录 C.7）
- **Observation**：C.7 声称"整条链路的状态全量持久化到 Redis + PostgreSQL，任何一步崩溃都能恢复"。设计层 C.6 其实有精准回滚目标 + MediatR 事件（合理），但蓝图**未规定每步结果落库**，该绝对承诺无证据支撑。
- **Why it matters**：绝对承诺若未兑现即成过度承诺。且此前代码审计已证实实现偏离——`WorkflowStateMachineEngine` 运行态仅存内存 `ConcurrentDictionary`、回滚为全量重置，正是这个蓝图过度承诺在落地时的失真。**蓝图层面的绝对措辞会纵容实现的偷工。**
- **Recommendation**：把承诺改为"每步结果落库、崩溃从中断步恢复"，并附 kill+restart 集成测试证明；保留 C.6 的精准回滚目标设计。

## What the blueprint gets right

- **DDD 分层 / DI / 依赖倒置**（§三、§七）：领域层零外部依赖、抽象在 `Application.Abstractions`、实现在 Infrastructure、DI 在 Api 层——教科书级。
- **精准回滚目标设计**（C.6）：回滚到"指定步骤"而非全量重置，方向正确（仅实现层曾漂移）。
- **事件驱动解耦**（C.7）：MediatR `WorkflowStepCompleted` 多 EventHandler 做步骤间解耦，利于扩展。
- **角色可扩展性**（C.8）：`AgentRole` 枚举 → `AgentType` 值对象，执行链路因已用 Guid+字典零改动——改造设计成熟。
- **可观测性铁律**（§八）：Decorator 无侵入埋点、Grafana 大盘、5 条告警规则——企业级。
- **安全优先**（§九）：沙箱网络隔离/资源限制/只读文件系统/非 root、Prompt 注入防护——第一优先级而非后补。

## Scope note

本评审仅评估**设计范式层**。实现是否忠于蓝图由 `ddd-code-reviewer` 把关（其 §0 路由已把 Phase 2 的 Module 2/Module 4 钉死）；DDD 结构由 `ddd-phase-quality-gate` 把关。三者职责：
- `blueprint-architecture-review` → 蓝图本身合不合理（动手前）
- `ddd-code-reviewer` → 代码是否偏离蓝图（Phase 2/高风险模块）
- `ddd-phase-quality-gate` → DDD 结构卫生（各阶段）

---

## 复审结论（蓝图附录 C 重写后 · 2026-07-16）

**Verdict: DESIGN READY（P1 全部闭环，P2 进入排期）**

附录 C 已按本评审 P1 项重写，逐条核对：

| # | 原严重级 | 蓝图处置 | 状态 |
| :--- | :--- | :--- | :--- |
| F1 拓扑纯线性 / 两模式二分 | P1 | C.2 合并为**单一编排原语** + `sequential`/`negotiation` 预设；线性降为退化特例 | ✅ 闭环 |
| F4 双上下文契约 | P1 | C.3 统一 **`WorkflowContext`** 契约，调度层与协商层同消费 | ✅ 闭环 |
| F2 无 critic 循环 | P1 | C.6 新增 **critic 循环**（结构化 diff 精准返修，非整轮重跑） | ✅ 闭环 |
| F3 无上下文伸缩 | P1 | C.3.1 新增**上下文伸缩策略**（Blackboard + 逐步摘要 + RAG 注入 + 单 Agent 封顶） | ✅ 闭环 |
| F9 恢复过度承诺 | P2→已软化 | C.7 改为「每步落库 + 中断步恢复 + kill+restart 测试证明」，删绝对措辞 | ✅ 闭环 |

**残余 P2（进入 Phase 排期，非阻断）**：
- F5 RAG 接地 / F6 HITL 断点 / F8 质量闭环硬化 → 已在 `phase-3-platformization.md`「蓝图对齐新增项」与 `phase-4-advanced-features.md` 任务清单排入，合入前强制 `ddd-code-reviewer`。
- F7 角色保真 → 仍偏 System Prompt 区分；建议后续按角色补工具集/知识源（可在 Phase 3 自定义 AgentType 时一并做）。

**下游动作（MANDATORY）**：
1. Phase 任务清单已同步（Phase 2 Module 4 改 negotiation 预设、Module 2 对齐统一契约；Phase 3 补 F3/F5/F6；Phase 4 补 critic/上下文策略）。
2. 旧蓝图下对 Module 2/4 的 `ddd-code-reviewer`「忠实 PASS」已作废——Phase 2 实现须以**新蓝图**为 spec 重卡（见 `phase-1 §0-1` 变更传播规则）。
