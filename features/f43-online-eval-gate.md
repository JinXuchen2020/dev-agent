# F43 · 在线评估门禁 + 部署闭环 设计文档（v1）
> **编号说明（2026-09-03 校正）**：本史诗原记 **F34**，与先到者「F34 · 沙箱双层隔离」（2026-08-07）撞号，现重编号为 **F43**。历史标识不变：分支名 `feat/f34-online-eval-gate` 与该次 `.quality-gate.json` 的 phase 仍为 f34-online-eval-gate。

> **关联**：`phases/phase-11-online-eval-gate.md`、`features/backlog.md` F34（v1 仅验收①）
> **状态**：`doing`（2026-08-25，分支 `feat/f34-online-eval-gate`，基于 f33）

---

## 1. 现状核实

| # | 事实 |
|---|------|
| ① | F24 已落地 `EvaluationDataset/EvaluationCase` 聚合 + `POST /api/v1/evaluation-datasets/{id}/run`（RunEvaluationCommand）——**离线手动触发**，无阈值判定、无阻断语义 |
| ② | RunEvaluation 天然影子隔离：每个 case 以一次性克隆工作流执行（新 GUID），不触碰生产定义 |
| ③ | EvaluationReport(Total, Passed, Score, Cases) 已含判定所需全部数据；AuditActionType 枚举字符串存储，可安全追加成员 |

## 2. v1 目标（backlog 验收①：在线 eval 门禁，纯应用层复用）

把评估运行升级为**带阈值判定的部署门禁**：CI / 发布流水线调用门禁端点，通过率未达阈值返回失败语义（HTTP 422），达成则 200——「真实阻断」而非仅报告。

### 明确不做（v1 边界，与 backlog 延后项一致）
- CI 流水线 YAML 接入样例（提供 curl 用法说明即可）
- 队列化执行 / 水平扩展 / 多 worker 租约消费（依赖分布式落点，独立排期）
- 在线监控告警聚合、异常回放诊断入口

## 3. 核心设计

```
POST /api/v1/evaluation-datasets/{datasetId}/gate/{workflowId}
body: { "minPassRate": 0.9 }        // 可选；缺省读配置
→ 200 { passed:true, minPassRate, score, total, passed, report:{...} }
→ 422 { passed:false, ...同构 }      // 门禁失败语义：流水线据此阻断
```

- **阈值解析链**：请求显式值 > `EvaluationSettings.GateMinPassRate`（新增，默认 0.8）
- **影子语义**：门禁复用 RunEvaluation 的一次性克隆路径——零生产状态写入，天然 shadow-safe
- **审计**：新增 `AuditActionType.EvaluationGate`（枚举字符串存储，无迁移），details 记录阈值与得分
- **空数据集语义**：Total=0 → Score=0 → 不通过（防「无数据即放行」漏洞）
- **命令复用**：Gate handler 内部委托 RunEvaluationCommandHandler（单一执行管线，不复制回归逻辑）

## 4. 测试计划
- Gate handler：≥阈值通过 / 低于阈值失败 / 显式阈值覆盖配置 / 空数据集不通过
- Api smoke：seed 数据集+工作流 → gate 失败返回 422 且 body.passed=false
- 回归四套件全绿

## 5. 完成记录（2026-08-25）

**分支**：`feat/f34-online-eval-gate`（基于 f33）

**交付物：**
- **① 在线评估门禁**：`RunEvaluationGateCommand` + handler——阈值解析链（请求显式 > `EvaluationSettings.GateMinPassRate` 默认 0.8）；执行委托 RunEvaluation（一次性克隆 = 影子隔离，零生产写入，单一执行管线不复制回归逻辑）
- **阻断语义**：Passed=false 时端点返回 HTTP 422（body 含完整报告），CI/发布流水线据此阻断部署；空数据集显式守卫恒不通过（防「无数据即放行」）
- **审计归因**：新增 `AuditActionType.EvaluationGate`（Aggregates 内生效枚举，字符串存储无迁移），details 记录 score vs threshold 与 PASS/BLOCK 判定
- **端点**：`POST /api/v1/evaluation-datasets/{datasetId}/gate/{workflowId}`（Admin/Operator，body 可选 minPassRate），remarks 含 CI curl 阻断用法示例
- **配置**：EvaluationSettings 新增 GateMinPassRate=0.8

**测试**：新增 `RunEvaluationGateCommandHandlerTests` 5 例——超阈值通过+审计断言、低于阈值阻断、显式阈值覆盖配置、空数据集零阈值仍拦、越界阈值抛 ArgumentOutOfRange。全绿 App226 / Infra154+6skip / Api35 / Arch9；build 0 警告 0 错误；前端零改动。

**质量门**：三道门 PASS，`.quality-gate.json` 推进 `f34-online-eval-gate`，`cleared:true`

**已知残留（与 backlog 延后项一致）**：CI YAML 样例接入、队列化执行/水平扩展（依赖分布式落点）、在线监控告警聚合、异常回放诊断入口——均独立排期。

---

## 二期收口说明

F34 v1 合入后，第二期「真 Agent Harness 升级」六史诗 F29–F34 全部 done。蓝图 §7「真 Harness」判定链路（自主循环 F29 / durable 执行 F30 / agent 实体化 F31 / 消息协作 F32 / 语义记忆 F33 / 在线评估门禁 F34）已全部落地；队列化水平扩展与监控告警聚合按计划延后独立排期。