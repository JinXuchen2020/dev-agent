# 阶段十一：在线评估门禁 + 部署闭环

> 学习目标：把 F24 的数据集回归从"离线"升级为"生产前 / 影子流量自动门禁 + 在线监控告警 + 队列化水平扩展"。本阶段是蓝图"真 Harness"判定（§7）的最后一块拼图。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.7 / §3 层4 / §4 Phase 11 / §5 D1/D2 / §6 / §7）、`./phase-7-durable-execution.md`（前置：durable 执行是队列化基础）、`./phase-10-semantic-memory.md`（记忆质量纳入在线 eval）、`../docs/quality/*`（质量门）。

## 学习目标

- [ ] **在线 Eval 门禁**：把 F24 `EvaluationDatasetsController POST /run` 变为生产变更前的自动回归门禁（含影子流量）
- [ ] **在线监控告警**：token / cost / latency 实时告警，成本归因到 agent / tenant
- [ ] **CI 自动回归**：eval 门禁挂 CI，失败阻断合入 / 部署
- [ ] **队列化执行**：引入执行队列，支持水平扩展（决策 D1 的分布式落点）
- [ ] **异常回放**：结合阶段七检查点 + 阶段五审计，支持失败执行回放诊断

## 前置依赖

- [ ] 阶段七执行持久化已完成（队列化执行依赖 durable 检查点，崩溃可恢复）
- [ ] 阶段十语义记忆已完成（记忆质量纳入在线 eval 数据集）
- [ ] 已确认 F24 现状（§1.7）：`ExecutionLogEntry` 含 `TokensIn/TokensOut` + `NodeType`；`EvaluationDataset` / `EvaluationCase` + `EvaluationDatasetsController POST /run` 已落地——本阶段在其上接"在线闭环"
- [ ] 已锁定蓝图决策 **D1（分布式落点）/ D2（可选 broker）**：队列化 / 水平扩展在此阶段落地（Phase 7-10 用 in-process 即可）

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [ ] 重核实 F24 评估设施（§1.7）：确认 `EvaluationDataset` / `EvaluationCase` 聚合与 `EvaluationDatasetsController POST /run` 已落地，但**仍是离线 / 手动触发**，无生产前自动门禁、无在线监控、无队列。

### 实现任务

- [x] **生产前 Eval 门禁**（F34 v1，2026-08-25）：`RunEvaluationGateCommand` 阈值解析链（显式 > 配置默认 0.8）+ 空数据集恒拦守卫；端点 `POST /evaluation-datasets/{id}/gate/{workflowId}` 通过 200 / 未达 **422 阻断**；审计 `AuditActionType.EvaluationGate`。详见 features/f34-online-eval-gate.md §5。🔍 强制 `ddd-code-reviewer`：核对门禁**真实阻断**（非仅报告）、阈值来自数据集定义、失败路径真实生效。
- [ ] **影子流量回归**：对生产流量做影子副本跑新版本，与基线比对输出差异，异常才拦截；不污染生产状态。🔍 强制 `ddd-code-reviewer`：核对影子流量隔离（不写生产、不影响在线）、差异判定真实生效。
- [ ] **在线监控告警**：基于 `ExecutionLogEntry` 的 token / cost / latency 实时聚合，成本归因到 agent / tenant；超阈值告警通道（阶段五 `AuditLog` / 通知）。🔍 强制 `ddd-phase-quality-gate`：核对指标聚合真实、告警通道真实存在、阈值可配。
- [ ] **CI 自动回归**：eval 门禁挂 CI 流水线，PR / 部署前自动跑，失败阻断。🔍 强制 `ddd-phase-quality-gate`：核对 CI 配置真实接入、失败阻断逻辑生效。
- [ ] **队列化执行 + 水平扩展**：引入执行队列（决策 D1/D2），执行请求入队、多 worker 消费，配合阶段七 durable 检查点实现崩溃恢复；支持水平扩容。🔍 强制 `ddd-code-reviewer`：核对队列消费幂等、多 worker 不重复驱动同一执行（复用阶段七租约）、崩溃可恢复。
- [ ] **异常回放**：结合阶段七检查点 + 阶段五 `AuditLog`，提供失败执行回放 / 单步重跑诊断入口。🔍 强制 `ddd-code-reviewer`：核对回放能 reconstruction 失败现场、单步重跑不污染已落库结果。

## 验收标准

1. 模型 / prompt / agent 配置变更合入前，自动跑 eval 数据集，未达阈值**阻断**部署（非仅告警）。
2. 影子流量对生产无副作用，异常差异可被检测并拦截。
3. token / cost / latency 在线监控可按 agent / tenant 归因，超阈值触发告警。
4. CI 流水线在 PR / 部署前自动 eval，失败阻断合入。
5. 执行经队列化，可水平扩容（≥2 worker 同时消费不重复驱动同一执行）；worker 崩溃经 durable 检查点恢复。

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。在线 Eval 门禁 / 队列化执行属"叙事性能力"，合入前强制 `ddd-code-reviewer`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性能力"的模块（在线 Eval 门禁 / 影子流量 / 队列化执行——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §3/§4 Phase 11、依赖是否真实使用、门禁是否真实阻断、队列是否真实幂等 |
| 纯基础设施 / 结构卫生模块（监控指标聚合 / CI 配置 / 队列基础设施 / DI / EF 映射） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 蓝图 §3 / §4 Phase 11 / 阶段十一验收标准"）。缺此项即视为未通过。

### Phase 11 强制范围（高风险叙事性模块）

- **在线 Eval 门禁 / 影子流量**：核对 §1.7 / §4 Phase 11；重点验证门禁真实阻断、影子流量生产隔离、差异判定真实生效。
- **队列化执行 + 水平扩展 / 异常回放**：核对 §3 层4 / §5 D1-D2 / §6；重点验证多 worker 幂等、崩溃可恢复、回放 reconstruction 真实。

> 规划提示：阶段十一是"真 Harness"判定的收口阶段（蓝图 §7 第 5、6 条），本 §0 要求在此阶段启动前即明确——上述模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-08-25（F34 v1）
- **完成日期**：v1 门禁 2026-08-25；其余任务（影子流量自动化/监控告警/CI 接入/队列化/异常回放）按 backlog 延后独立排期
- **完成度**：██░░░░░░░░ ~17%（v1 验收①门禁已落地）

## 回顾（完成后填写）

### 做得好的
- v1 聚焦单一验收①，复用 RunEvaluation 克隆管线零复制回归逻辑，影子隔离白嫖
- 空数据集显式守卫堵住「无数据即放行」漏洞；422 阻断语义对流水线友好
- 审计新增 EvaluationGate 动作，score vs threshold 全留痕

### 下次改进
- 队列化落地时复用 F30 租约机制防多 worker 重复驱动
- CI YAML 样例与门禁端点一并发布更顺滑

### 对蓝图文档的反馈
- §Phase 11 六任务体量实际是一个完整季度；F34 按 backlog 拆出 v1 单验收是正确切分
