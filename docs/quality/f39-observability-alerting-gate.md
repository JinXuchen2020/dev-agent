# F39 · 监控告警聚合（可观测性栈）质量门报告

> 日期：2026-09-02 · 分支 `feat/f39-observability-alerting`（基于 `feat/f38-ci-eval-gate`）· feature-builder 流水线
> 设计文档：`features/f39-observability-alerting.md`（§5 决策 D1–D4 锁定、§8 审查修复记录、§9 Quality Gate Checklist）

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer | **PASS**（0 open） | P1×2 + P2×3 + P3×5 修复 |
| ddd-phase-quality-gate | **PASS**（P0=P1=P2=0，P3×3 waiver） | checklist 嵌入设计文档 §9 |
| codebase-optimizer | **PASS**（Round F39-01，0 open） | 修 3×P2 文档同步，2×P3 waiver |

## 范围

不同于 F38（纯模板），F39 因决策 **D1=B** 含**后端埋点改动**：`IExecutionQueue.QueueDepth` 契约、`WorkflowMetrics.EvaluationGateCounter`（`evaluation.gate.total{passed}`）、`execution.queue.depth{backend}` ObservableGauge（`QueueDepthGauge`，静态 Meter 注册 + 闭包捕获实例，无静态可变状态）；以及 `deploy/` 监控栈与 `docs/observability-guide.md`。

## ddd-code-reviewer 修复（关键项）

| 严重度 | 位置 | 问题 | 修复 |
|---|---|---|---|
| P1 | RedisStreamExecutionQueue | `XACK` 不减 `XLEN`，「积压」随历史流量单调增长 → `QueueBacklogHigh` 假告警 | `CompleteAsync` 改为 XACK + XDEL（单消费组安全；先 ack 后删，删除失败只虚增积压不丢任务） |
| P1 | grafana provisioning | 面板按 `datasource.uid="prometheus"` 引用，但数据源未固定 uid → **12 面板全部 data source not found** | 数据源显式 `uid: prometheus` |
| P2 | alertmanager.yml | `inhibit_rules.equal:['instance']` 在聚合后无 instance → 抑制永不生效 | `equal: []` |
| P2 | alert-rules.yml | Stalled 兜底 `sum(...)==0` 对空向量永不触发 | `or vector(0)` |
| P2 | 注释/文档 | 「Dispose 时释放仪表」「active_steps 无写入方」等表述与实现不符 | 全部如实改写（.NET `ObservableGauge` 无独立 Dispose；`workflow_active_steps` 确有写入方但不入告警/面板） |
| P3×5 | rules / dashboard / metrics | humanizeDuration 单位误读、legendFormat 指向不存在标签、`result="failed"` 口径注释、absent 文案 | 逐条修正 |

## 最重要的两个「假绿」校正（相对 backlog 原文）

1. **不存在 `result="failed"`**：编排器失败走回滚，指标只有 `success|rolledback`。若照直觉写 `failed`，`ExecutionFailureRateHigh` 永不触发（静默假绿）。现口径 `rolledback` 占比 + API 错误率独立告警（D4=A）。
2. **队列积压不能只看 XLEN**：见上表 P1；并把深度改为**应用自报**（D1=B），使 InMemory 后端也可观测。

## 验证

- 代码：`dotnet build AgentPlatform.sln` **0 警告 0 错误**；Application **269/269**、Infrastructure **174 通过 + 8 跳过**（Docker 门控）、Api **39/39**、Architecture **9/9**、Integration **5/5**（需 `OPENAI__Key`）、SpecFlow **115/116**（唯一失败 = master 既有 LLM 用例）。
- 新增测试以 `MeterListener` **断言真实测量事件**（门禁 `passed=true/false` 两分支、队列深度值与 `backend` 标签、Redis 未连接时 `QueueDepth=0` 不抛），而非「方法存在即正确」。
- 配置：6 个 monitoring YAML + dashboard JSON 全量结构校验；专用脚本核验**面板与 9 条告警表达式引用的指标名/标签值全部存在于代码埋点**（白名单来自文件:行号），杜绝臆造指标。
- 本机无 Docker：`promtool check` / `amtool check-config` / Grafana 导入未实跑，指南 §1/§8 留有可复现校验命令；已作为残留记录。

## 已知残留（非阻断）

1. `workflow_id` / 含 GUID 的 `path` 标签为高基数风险，治理（path 归一化为路由模板、workflow_id 转 trace）列为独立技术债。
2. RabbitMQ 深度为 ≤5s 缓存值（其管理调用无同步廉价形式）；Redis/InMemory 为精确值。
3. `workflow_active_steps` 虽非全链路写入，本栈不为其建面板/告警，待编排器覆盖完整后再接入。
4. redis-exporter 属可选 profile，未启用时该 target 为 down（无对应告警，设计接受）。
