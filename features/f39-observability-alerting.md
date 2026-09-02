# F39 · 监控告警聚合（可观测性栈）设计文档

> 来源：F34 评估门禁 · 延后项。
> 风险等级：🟡 中风险（Grafana Dashboard JSON 维护成本；告警阈值合理性）。
> 分支：`feat/f39-observability-alerting`（2026-09-02 自 `feat/f38-ci-eval-gate` 新建——用户指定基线）。

## 1. 目标

把现有「裸 `/metrics` + 半成品监控栈」升级为**可用**的可观测性栈：Prometheus 抓取 + 告警规则 + Grafana 仪表盘（真正能导入生效）+ 告警通知（Slack/PagerDuty）+ 一键部署与指南。核心告警四件套：执行失败率、门禁阻断率、队列积压、模型调用延迟。

## 2. 代码现状（调研事实，2026-09-02）

**埋点与端点（已存在）**

| 事实 | 位置 |
|---|---|
| Prometheus 导出器已启用（`AddPrometheusExporter` + `AddMeter(DiagnosticsConfig.ServiceName)` + `AddMeter(WorkflowMetrics.MeterName)`） | `Api/Configuration/InfrastructureConfiguration.cs:84-87` |
| 抓取端点 `app.MapPrometheusScrapingEndpoint("/metrics")` | `Api/Program.cs:126` |
| API 计数器/直方图（OTel 点号 → Prometheus 下划线） | `Api/Diagnostics/DiagnosticsConfig.cs:37-48` |
| 工作流/模型指标 | `Application/Diagnostics/WorkflowMetrics.cs` |
| 已存在的 `deploy/docker-compose.monitoring.yml`（prometheus + grafana，镜像 `latest`）与 `deploy/monitoring/prometheus.yml`（单 job、**无 rule_files / 无 alertmanager**）、`deploy/monitoring/grafana-dashboard.json`（7 面板） | `deploy/` |

**真实指标名与标签（PromQL 必须严格对齐）**

| 指标（Prometheus 侧） | 标签 | 来源 |
|---|---|---|
| `api_requests_total` | `path`, `method`, `status_code` | `MetricsMiddleware.cs:41-44` |
| `api_errors_total` | `path`, `status_code` | `MetricsMiddleware.cs:52-54` |
| `api_request_duration_ms_bucket` | `path`, `method` | `MetricsMiddleware.cs:46-48` |
| `workflow_completed_total` | **`result="success"｜"rolledback"`**, `workflow_id` | `WorkflowCompletedEventHandler.cs:77-80`、`WorkflowRolledBackEventHandler.cs:78-81` |
| `workflow_step_duration_ms_bucket` | `step_name`, `workflow_id` | `StepCompletedEventHandler.cs:94-96` |
| `model_call_total` | `provider`, `model` | `SemanticKernelModelClient.cs:326-328` |
| `model_call_duration_ms_bucket` | `provider`, `model` | `SemanticKernelModelClient.cs:330-332` |
| `workflow_active_steps` | — | 定义存在，未见生产调用（视需要标注） |

**关键校正（相对 backlog 原文）**

1. **不存在 `result="failed"`**：失败路径经回滚落 `result="rolledback"`。若照 backlog 直觉写 `result="failed"`，该告警**永不触发**（静默假绿）。执行失败率须按 `rolledback`（可选叠加 API 5xx）计算。
2. **门禁阻断率无需新埋点**：F34 门禁未通过返回 **422**，而 `api_requests_total` 已带 `path`+`status_code` → 可直接派生
   `rate(api_requests_total{path=~".*evaluation-datasets.*/gate/.*",status_code="422"}[5m]) / rate(api_requests_total{path=~".*evaluation-datasets.*/gate/.*"}[5m])`。
3. **队列积压无需触后端**：F37 队列无自定义指标，但真实后端可从中间件自身取指标——Redis 侧用 `redis_exporter`（`redis_stream_group_pending_messages` / XLEN 派生），RabbitMQ 侧自带 Prometheus 插件（`rabbitmq_queues_total_ready` / `queues_unacked`）。InMemory 后端无跨进程指标（文档明确标注为不可告警）。
4. **既有 Grafana 挂载方式无效**：compose 把裸 `grafana-dashboard.json` 挂到 `/etc/grafana/provisioning/dashboards/dashboard.json`——Grafana provisioning 需要 **provider YAML + 存放 dashboard JSON 的目录**，当前挂法不会加载面板（违反验收「可导入且面板无报错」）。需修成 `provisioning/dashboards/default.yaml` + `provisioning/dashboards/json/*.json` + Prometheus datasource 自动配置。
5. **`latest` 镜像未锁版本**：Dashboard JSON 跨大版本易断面板（backlog 风险项）→ 固定版本并在指南声明 Grafana/Prometheus 版本要求。

**已知运维隐患（记录，不在本 feature 改后端）**：`workflow_id` / `path`（含 GUID 的路径）作为标签有**高基数**风险；本 feature 的告警表达式一律做无该标签的聚合，并在指南标注技术债。

## 3. 交付物（全部在 `deploy/` 与 `docs/`，不触 `src/`）

> **实施校正（2026-09-02，第三道质量门）**：决策 D1 最终锁定为 **B**（非本节原文建议的 A），故本 feature **确有 `src/` 后端改动**——上文「不触 `src/`」及 §5/§6/验收 #6 的「后端零改动、既有测试基线不受影响」表述已过期，以本校正为准。实际落地：`IExecutionQueue.QueueDepth` 契约 + 三后端真实读数、`WorkflowMetrics.EvaluationGateCounter`（`evaluation.gate.total{passed}`）、`execution.queue.depth{backend}` ObservableGauge（`QueueDepthGauge`），并新增 `EvaluationGateMetricsTests` / `QueueDepthMetricsTests`（详见 §8、§9 及 CHANGELOG v2.38）。收益：门禁阻断率取语义最精确埋点、队列积压三后端（含 InMemory）均可观测。回归基线：`dotnet build` 0/0，Application/Infrastructure/Api 测试全绿（见 §8 验证段）。

1. `deploy/monitoring/prometheus.yml`（改造）：scrape 目标参数化（API / redis_exporter / rabbitmq）、`rule_files` 挂 `alert-rules.yml`、`alerting.alertmanagers` 配置、保留 `evaluation_interval`。
2. `deploy/monitoring/alert-rules.yml`（新）：四组告警（`ExecutionFailureRateHigh` >10%、`EvalGateBlockRateHigh` >5%、`QueueBacklogHigh` >100、`ModelLatencyP99High` >30s）+ 辅助告警（`TargetDown`、`ApiErrorRateHigh`、`ApiLatencyP95High`），每条含 `severity`/`summary`/`description` 模板与合理 `for` 持续窗口。
3. `deploy/monitoring/alertmanager.yml`（新）：route + Slack / PagerDuty / webhook 接收器（占位 secret 注入，模板不含真实密钥）。
4. `deploy/monitoring/grafana/provisioning/{datasources,dashboards}/*.yaml` + `deploy/monitoring/grafana/dashboards/agent-platform.json`（面板扩充：执行量趋势 / 成功率 / **门禁 422 阻断率** / 模型延迟 P50-P95-P99 / 队列积压 / API 错误率与延迟 / step 耗时 Top），保持向后兼容的 schema 版本。
5. `deploy/docker-compose.monitoring.yml`（改造）：修 Grafana provisioning 挂载 + 锁镜像版本（`prom/prometheus:v2.54.1`、`grafana/grafana:11.2.0`、`prom/alertmanager:v0.27.0`、`oliver006/redis_exporter:v1.64.0`）+ 新增 alertmanager 与 redis_exporter 服务 + 健康检查 + 可选 rabbitmq 插件端口注释。
6. `docs/observability-guide.md`（新）：一键部署、指标清单（真实名与标签）、告警阈值与调参、Slack/PagerDuty 对接、Grafana 手工导入与版本要求、故障排查（无数据/Target down/面板空/告警不触发）、高基数与 `workflow_id` 标签技术债、InMemory 队列不可告警说明、与 F37（队列）/F34（门禁）契约变更时的同步义务。

## 4. 验收标准

1. `prometheus.yml` 与 `alert-rules.yml` 通过 `promtool check config` / `promtool check rules`（本机若无 promtool，则以 `yaml.safe_load` 结构校验 + 规则字段断言替代，并在指南写明校验命令）。
2. Grafana Dashboard JSON 可导入且面板无报错（结构校验：`panels[].targets[].expr` 指标名全部存在于 §2 清单；provisioning 文件布局正确）。
3. 所有 PromQL 只引用**真实存在**的指标名与标签值（尤其 `result="rolledback"` 而非 `failed`；422 派生门禁阻断率）。
4. 告警阈值合理并给出依据（如 `for: 5m` 避免抖动；队列 100 对应 F37 InMemory 容量 256 的 40%）。
5. 指南完整：一键部署 / 指标清单 / 阈值调参 / 告警对接 / 故障排查 / 版本要求。
6. 不触 `src/`（后端零改动，既有测试基线不受影响）。三道质量门全绿；`.quality-gate.json` 推进 `f39-observability-alerting`（`cleared:true` + `codebaseOptimizer`）；质量报告 `docs/quality/f39-observability-alerting-gate.md`。
7. 文档同步：CHANGELOG、`appendices/deployment-devops.md`、backlog F39 → done。

## 5. 决策（已锁定，2026-09-01 用户拍板；原建议与最终选择差异见下）

- **D1 队列/门禁指标来源 = B（用户选，原建议 A）**：A) 零后端改动派生（门禁 422 由 `api_requests_total` 派生；队列用 redis_exporter / RabbitMQ 自带插件），InMemory 模式明确标注不可告警；B) 额外补后端原生埋点（门禁计数器 + 队列深度 ObservableGauge，约 2 个文件，语义更显式且 InMemory 也可观测）。**建议 A**（符合 backlog「不触后端代码」；B 会扩大范围并偏离既定边界）。
- **D2 = A（纳入）**：A) 纳入（完整闭环，符合目标「异常自动通知」）；B) 只做 Prometheus 原生告警规则，通知留后续。**建议 A**。
- **D3 = A（修布局+扩面板）**：A) 修 provisioning 布局 + 扩面板 + 锁版本（交付「打开即见」）；B) 只补文档说明现状问题。**建议 A**（否则验收 2 无法成立）。
- **D4 = A（rolledback + 5xx 独立）**：A) `rolledback` 占比（贴合代码事实：失败⇒回滚）；B) 叠加 API 5xx（更宽，但混入非工作流故障）。**建议 A 为主 + 5xx 作为独立辅助告警**。

## 6. 风险

- 🟡 Dashboard JSON 与 Grafana 版本耦合 → 锁 `grafana:11.2.0` 并在指南写明升级需回归面板。
- 🟡 阈值经验值（10%/5%/100/30s）需按实际流量校准 → 指南给调参方法与「先 `severity: info` 观察一周」建议。
- 🟢 不触后端；`promtool`/Grafana 无法本机实跑 → 以结构校验 + mock 校验兜底，指南提供可复现验证命令。

## 8. 审查修复记录（F39 对抗式审查，2026-09-02）

| # | 严重度 | 文件:行 | 问题 | 修复 |
|---|---|---|---|---|
| 1 | P1 | RedisStreamExecutionQueue.CompleteAsync | `QueueDepth=XLEN` 名不副实：XACK 不删条目、XLEN 只随 MAXLEN≈100k 修剪，深度随历史流量单调增长 → `QueueBacklogHigh` 对已清空队列假告警、真实积压不可见 | ack 后同步 `XDEL`（单消费组 ap-workers，安全；先 ack 后删不丢任务；删除失败仅 debug 记虚增）。同步修正 QueueDepth/类头注释与 `IExecutionQueue.QueueDepth` 契约文档 |
| 2 | P1 | grafana/provisioning/datasources/prometheus.yml | 面板 JSON 全部以 `datasource.uid="prometheus"` 引用，provisioning 未显式 uid → Grafana 自动生成 uid，12 面板全报 data source not found | 数据源加 `uid: prometheus` |
| 3 | P2 | alertmanager.yml inhibit_rules | `equal: ['instance']` 死配置：规则表达式经 sum/max by 聚合后告警不带 instance → 抑制永不生效 | 改 `equal: []`（TargetDown firing 时抑制全部 warning/info，符合根因优先意图） |
| 4 | P2 | alert-rules.yml WorkflowExecutionStalled | `sum(increase(...))==0` 对空序列返回空向量 → 「指标从未出现」这一最严重场景下兜底告警自身永不触发 | `(sum(increase(...[30m])) or vector(0)) == 0` |
| 5 | P2 | QueueDepthGauge.cs 注释 | 原注释「无独立 Dispose、不提供注销路径」在 net9 事实上正确（已实测 `ObservableGauge` 无公开 Dispose），但缺泄漏/测试污染后果说明 | 保留 void Register 语义，注释补记：静态 Meter 终身持有闭包引用，生产单例无碍；测试中重复构造队列会累积陈旧同标签仪表（断言已用 Contains 容忍）；WorkflowMetrics 文档注释「并在 Dispose 时释放」不实，已改 |
| 6 | P3 | alert-rules.yml ModelLatencyP99High | `$value` 为毫秒但 `humanizeDuration` 按秒解读（30000 → "8.3h"） | description 改为 `{{ $value }} ms` |
| 7 | P3 | agent-platform.json legendFormat | 12 面板中 9 处 legend 指向不存在/被聚合掉的标签（`{{instance}}`/`{{label}}`）→ 图例空白；step 面板未按 step_name 拆分 | 逐面板修正（`{{result}}`/`{{provider}}`/`{{passed}}`/`{{backend}}`/`{{step_name}}` 或字面量）；面板 6 改 `sum by (le, step_name)` 落实「step 耗时 Top」交付项 |
| 8 | P3 | prometheus.yml / observability-guide.md | 「workflow_active_steps 无生产写入方」不实（Sequential/Negotiation 编排器 Record(1/0)，失败路径漏记 0） | 文档改为如实说明不设面板/告警的原因是 1/0 Histogram 不可聚合为活跃步数 |
| 9 | P3 | alert-rules.yml QueueDepthMetricAbsent + 指南 §8 | 「QueueEnabled=false 时 absent 属预期」不实：队列单例（ExecutionWorker 恒注册）无条件构造并注册 gauge | 告警 description 与指南排查行改为「缺失=版本早于 F39 或抓取失败」 |
| 10 | P3 | WorkflowMetrics.cs:45 | 既有注释「result (success/failed/rolledback)」诱发 F39 §2 明令禁止的 `failed` 口径 | 注释改 success/rolledback 并标注 F39 告警口径 |

关键不变量复核（VERIFIED）：`result` 仅 success/rolledback（两 handler 唯一写入点）；`passed="true"/"false"` 且 MeterListener 断言真实测量；backend 三值拼写与 `Backend` 属性逐一对应；OTel 点号→下划线映射（evaluation_gate_total / execution_queue_depth）与导出器规则一致；rule_files 路径与 compose 挂载一致；`/api/v1/...` 与门禁 `{id}/gate/{workflowId}` 正则匹配真实路由；QueueDepth 三实现均同步不抛（Redis 未连接/异常→0，Rabbit 读缓存，InMemory Reader.Count 不含在飞）；RabbitMQ 刷新循环 OCE/异常均被捕获、Dispose 先停循环后释 gate（≤2s 竞态窗口有 ODE 兜底捕获，可接受）；`IExecutionQueue` 全部 5 实现含两个测试替身均补 QueueDepth；workflow_active_steps 未被任何告警/面板引用。

验证：`dotnet build AgentPlatform.sln` 0 警告 0 错误；Application.Tests 269/269（连续两轮，首轮 1 例为无关既有 flake，复跑未复现）；Infrastructure.Tests 174 通过/8 跳过（Docker 门控）；Api.Tests 39/39。monitoring 全部 YAML/JSON `yaml.safe_load`/`json.load` 结构校验通过（9 规则、12 面板、inhibit equal=[] 生效）。promtool/amtool/镜像 tag/wget healthcheck 本机无 Docker 无法实跑——按 §6 风险项以文档校验命令兜底。

## 9. Quality Gate Checklist（F39）

> 8 类齐全，条目对齐本 feature 实际模块。F39 已完成并通过对抗式审查 + Mode 3 质量门复核，故已核项以 [x] 标注。

### 1. Pre-flight Version Audit
- [x] 镜像版本全部锁定：`prom/prometheus:v2.54.1`、`grafana/grafana:11.2.0`、`prom/alertmanager:v0.27.0`、`oliver006/redis_exporter:v1.64.0`（compose 已固化，指南声明版本要求）
- [x] 埋点 API 以 net9 实测为准：`ObservableGauge` 无公开 Dispose（§8#5），据此定义生命周期策略
- [x] `dotnet build` 先于新码通过；OTel 点号→Prometheus 下划线映射规则已核（`evaluation.gate.total`→`evaluation_gate_total`、`execution.queue.depth`→`execution_queue_depth`）

### 2. BDD / 测试先行（本 feature 以 MeterListener 单测兜底验收）
- [x] `EvaluationGateMetricsTests`：门禁 passed=true/false 两态真实产出测量事件（断言标签值，非「方法存在即正确」）
- [x] `QueueDepthMetricsTests`：InMemory 深度随入队/消费变化 + Gauge 带 `backend="InMemory"` 标签 + Redis 无连接返回 0 不抛
- [x] 边界覆盖：Redis 未连接（返回 0）、Rabbit 刷新降级（保留旧值）、空序列兜底告警 `or vector(0)`

### 3. DDD Layer Rules
- [x] `QueueDepth` 契约置于 `Application.Abstractions/IExecutionQueue`（能力抽象，非实现细节）；三后端各自实现于 `Infrastructure/Queues`
- [x] `QueueDepthGauge` / `WorkflowMetrics` 埋点方向正确：Infrastructure→Application（与 `SemanticKernelModelClient`、EventHandlers 既有模式一致），无反向依赖
- [x] Domain 项目零外部依赖未受影响；`RunEvaluationGateCommandHandler` 埋点留在 Application 同层

### 4. DI Registration Completeness
- [x] 无新增接口需注册；`QueueDepthGauge` 为静态注册器（各队列构造期自注册），无需 DI 注册
- [x] 新仪表（`EvaluationGateCounter`、`execution.queue.depth` Gauge）均建于 `WorkflowMetrics.Meter`，已被 `AddMeter(WorkflowMetrics.MeterName)`（`InfrastructureConfiguration.cs:85`）覆盖
- [x] Rabbit 后台刷新 Task 随队列（单例）构造启动，容器释放时 `DisposeAsync` 停循环并释放 cts

### 5. Configuration-First
- [x] 队列容量沿用 `DurableExecutionSettings.QueueCapacity`（256，既有配置），阈值 100 依据其 40% 并在指南记录
- [x] 抓取目标 / 阈值 / 数据源 URL 均在 `deploy/monitoring/*.yml` 配置层，不硬编码进业务代码
- [x] `rule_files: /etc/prometheus/alert-rules.yml` 与 compose 挂载路径一致；`alertmanager` target `alertmanager:9093` 一致

### 6. EF Core Mapping Sync
- [x] N/A — F39 无聚合/实体/值对象变更，零迁移（仅只读观测属性 + 埋点 + 配置）

### 7. Concurrency & Lifecycle
- [x] Rabbit `_depth` 经 `Volatile.Read/Write` 保护；刷新经 `_gate` 串行化；Dispose 先停循环后释闸（≤2s 竞态有 ODE 兜底捕获）
- [x] InMemory `_channel.Reader.Count` 线程安全同步读；Redis QueueDepth 走 `IsConnected` 守卫 + try/catch→0，绝不建连/抛出
- [x] Gauge 回调闭包捕获 DI 队列实例，杜绝静态可变状态；陈旧仪表以 Contains 断言容忍（§8#5）
- [x] 接受项（P3 documented）：Redis 同步 `StreamLength` 在 scrape 线程受 SyncTimeout 限界（net9 无异步 gauge 可用）；`5s` 刷新周期以注释/契约/指南文档化

### 8. Cross-Cutting Infrastructure（配置/安全/文档）
- [x] alertmanager 仅占位密钥（`REPLACE_ME`/`REPLACE_PAGERDUTY_INTEGRATION_KEY`），无真实凭据泄漏；v0.27 字段合法（route.matchers 串式、inhibit source/target_matchers、`equal: []`）
- [x] compose 宿主端口暴露（9090/9093/3000/9121）与 Grafana 匿名 Viewer 的生产加固在指南标注
- [x] prometheus/alertmanager 官方镜像为 busybox:glibc 基底，含 `wget`，healthcheck 不会永挂
- [x] 前端零改动（diff 确认）；`docs/observability-guide.md` 覆盖部署/指标清单/阈值调参/对接/排查/版本要求/技术债；CHANGELOG、`appendices/deployment-devops.md`、backlog F39 同步

### 增量门序（本 feature 模块）
```
Module 1: Application 埋点与队列抽象（IExecutionQueue.QueueDepth / WorkflowMetrics / RunEvaluationGate）
  → build 0 警告 → Application.Tests 绿 → DDD/DI 复核
Module 2: Infrastructure 三后端深度读数与仪表（InProcess/Redis/Rabbit + QueueDepthGauge）
  → build 0 警告 → Infrastructure.Tests 绿 → 并发/生命周期复核
Module 3: Grafana / Prometheus / Alertmanager 配置
  → YAML/JSON 结构校验 → 指标名/标签/路径一致性复核
Module 4: 文档与版本要求（observability-guide / CHANGELOG / deployment-devops / backlog）
  → 端到端人工核对 → 无新增 P0/P1
```
