# F39 · 可观测性栈部署与告警指南

平台指标（OpenTelemetry → Prometheus）+ 告警规则 + Grafana 仪表盘 + Alertmanager 通知的一键部署与运维说明。

相关文件：

| 文件 | 作用 |
|---|---|
| `deploy/docker-compose.monitoring.yml` | Prometheus + Alertmanager + Grafana（+ 可选 redis-exporter） |
| `deploy/monitoring/prometheus.yml` | 抓取与规则装载 |
| `deploy/monitoring/alert-rules.yml` | 9 条告警规则 |
| `deploy/monitoring/alertmanager.yml` | 路由 / Slack / PagerDuty / 抑制规则（模板，密钥占位） |
| `deploy/monitoring/grafana/provisioning/**` | 数据源与 dashboard provider 自动配置 |
| `deploy/monitoring/grafana/dashboards/agent-platform.json` | 12 个面板 |

设计文档：`features/f39-observability-alerting.md`。

---

## 1. 一键部署

```bash
# 1) 平台 API 需在跑（指标来源），默认端口 5000
dotnet run --project src/AgentPlatform.Api          # 或已部署实例

# 2) 起监控栈
docker compose -f deploy/docker-compose.monitoring.yml up -d

# 3) 打开
#    Prometheus   http://localhost:9090    （Status → Targets 看抓取是否 up）
#    Alertmanager http://localhost:9093    （看 firing/silence）
#    Grafana      http://localhost:3000    （左侧 Agent Platform 文件夹，面板开箱即见）
```

改过规则/配置后热加载：

```bash
docker exec ap_prometheus wget -qO- --post-data='' http://localhost:9090/-/reload
# 或 docker compose -f deploy/docker-compose.monitoring.yml restart prometheus
```

需要 Redis 自身指标（可选，仅 `QueueBackend=RedisStream` 时有意义）：

```bash
docker compose --profile redis -f deploy/docker-compose.monitoring.yml up -d
```

## 2. 指标清单（真实埋点，勿臆造）

| 指标 | 标签 | 含义 |
|---|---|---|
| `api_requests_total` | `path`, `method`, `status_code` | API 请求量 |
| `api_errors_total` | `path`, `status_code` | API 失败量 |
| `api_request_duration_ms_bucket` | `path`, `method` | API 延迟直方图 |
| `workflow_completed_total` | **`result`=`success`\|`rolledback`**, `workflow_id` | 工作流终态计数（**没有 `failed`**，失败⇒回滚） |
| `workflow_step_duration_ms_bucket` | `step_name`, `workflow_id` | 节点耗时 |
| `model_call_total` | `provider`, `model` | 模型调用量 |
| `model_call_duration_ms_bucket` | `provider`, `model` | 模型调用延迟 |
| `evaluation_gate_total` | `passed`=`true`\|`false` | F34 门禁判定（F39 埋点） |
| `execution_queue_depth` | `backend`=`InMemory`\|`RedisStream`\|`RabbitMQ` | F37 队列积压（F39 埋点） |

`workflow_active_steps` 有写入方（编排器按步开始/完成记 1/0），但作为 Histogram 无法如实聚合为「活跃步数」（失败路径不记 0），故**不设**面板与告警（避免假绿误导）。

## 3. 抓取要求

- 平台侧无需额外配置：OTel Prometheus 导出器 + `/metrics`（`Program.cs` `MapPrometheusScrapingEndpoint`），Meter 名已 `AddMeter` 注册。
- 容器内抓宿主 API 依赖 `host.docker.internal:5000`（Linux 由 compose 的 `extra_hosts: host-gateway` 提供）。若 API 与监控不同机，改 `prometheus.yml` 的 targets。
- 多实例部署时每个 API 实例都应被抓（新增 target 即可）；已用 relabel 固定 `instance=agent-platform-api` 以免面板序列随容器重启割裂——多实例请改为按 target 自动 instance。

## 4. 告警清单与阈值

| 告警 | 口径 | 阈值 / 持续 | 级别 |
|---|---|---|---|
| `ExecutionFailureRateHigh` | rolledback / 全部完成（15m） | >10% / 10m | warning |
| `WorkflowExecutionStalled` | 30m 无任何完成 | =0 / 30m | info |
| `ModelLatencyP99High` | 模型调用 P99 | >30s / 10m | warning |
| `ApiErrorRateHigh` | `api_errors_total`/`api_requests_total` | >5% / 10m | warning |
| `EvalGateBlockRateHigh` | `passed="false"` 占比（30m） | >5% / 15m | warning |
| `EvalGateHttp422RateHigh` | 门禁端点 422 占比（交叉验证） | >5% / 15m | info |
| `QueueBacklogHigh` | `execution_queue_depth`（按 backend） | >100 / 5m | warning |
| `QueueDepthMetricAbsent` | `absent(execution_queue_depth)` | 15m | info |
| `TargetDown` | `up{job="agent-platform-api"}==0` | 3m | critical |

调参建议：
- 队列 100 对应 F37 `QueueCapacity` 默认 256 的约 40%；改了容量请同步改阈值。
- 新接入先按 `severity: info` 观察一周，再决定是否升 warning/critical（避免噪声导致告警被忽略）。
- 比率型表达式分母用 `clamp_min(..., 1e-9)` 防「零流量 → NaN」误触发。

## 5. 通知对接（Alertmanager）

`deploy/monitoring/alertmanager.yml` 是**模板**，两处占位需替换：

```yaml
slack_configs:
  api_url: 'https://hooks.slack.com/services/REPLACE_ME'   # Slack Incoming Webhook
pagerduty_configs:
  routing_key: REPLACE_PAGERDUTY_INTEGRATION_KEY            # PagerDuty Integration Key
```

安全要求：**不要**把真实密钥提交进仓库。两种做法：
1. 部署时由 CI/CD 或配置管理（Ansible/Helm secrets）渲染该文件后再挂载；
2. 用 docker secret 覆盖挂载路径。

路由策略：`critical` → PagerDuty + Slack；其余 → 仅 Slack。已配 `inhibit_rules`：`TargetDown` 触发时抑制同源 warning/info（根因优先，防告警雪崩）。改完 reload：`docker exec ap_alertmanager wget -qO- --post-data='' http://localhost:9093/-/reload`。

## 6. Grafana 面板

provisioning 采用**正确布局**（provider YAML + JSON 目录分离）：

```
grafana/provisioning/datasources/prometheus.yml   # 数据源 → http://prometheus:9090
grafana/provisioning/dashboards/default.yml       # provider，指向 /var/lib/grafana/dashboards
grafana/dashboards/agent-platform.json            # 12 个面板
```

12 面板覆盖：API 请求量/错误率/P95、工作流成功率、终态速率、节点耗时 P95、模型调用量/分位数、门禁判定与阻断率、队列积压（含 >100 阈值线）、目标存活。

匿名访问默认为 `Viewer`（只读）。生产请关闭 `GF_AUTH_ANONYMOUS_ENABLED` 并接 SSO。

## 7. 版本要求（Dashboard 兼容性）

面板按 **Grafana 11.2.x / schemaVersion 39** 生成，Prometheus **v2.54.x**，Alertmanager **v0.27.x**。compose 已锁 tag，避免 `latest` 漂移：
- 升 Grafana major 时需回归面板（timeseries → 面板类型与 fieldConfig 结构可能变动）；
- 升 Prometheus 到 3.x 时需复核 `absent()`、`matchers` 语法与 alertmanager 配置格式。

## 8. 故障排查

| 症状 | 排查 |
|---|---|
| Targets 页 API 为 DOWN | API 未起 / 端口非 5000 / Linux 缺 host-gateway；宿主机 `curl localhost:5000/metrics` 先自证 |
| 有 target 但指标为空 | `AddMeter` 未覆盖到对应 Meter（`InfrastructureConfiguration.cs`）；或对应链路未被触发 |
| Grafana 面板全空 | 数据源是否 `prometheus:9090`；provider 路径挂载是否按 §6（旧布局不加载）；`docker logs ap_grafana` 看 provisioning 报错 |
| 告警永不触发 | 表达式是否引用了不存在的标签值（典型：`result="failed"`）；先在 Prometheus Explore 手跑 expr |
| 比率告警一直 firing | 流量过低导致抖动：加大 `for`、拉长 rate 窗口，或加 `sum(rate(...)) > 阈值` 的最低流量门限 |
| `QueueDepthMetricAbsent` 常报 | 队列仪表与 `QueueEnabled` 无关、随进程启动即注册——缺失说明平台版本早于 F39 或 `/metrics` 抓取失败（对照 `TargetDown`），而非「队列未启用属预期」 |
| 队列深度长期为 0 但有积压 | InMemory 后端为进程内视角，多实例互不可见；分布式部署请用 RedisStream/RabbitMQ |

## 9. 已知限制与后续

1. **高基数标签**：`workflow_id` / `path`（含 GUID 的路径）作为标签会带来序列膨胀。本栈所有告警/面板都做了去标签聚合；治理方案（path 归一化为路由模板、workflow_id 移出指标改用 trace）应作为独立技术债处理。
2. `execution_queue_depth` 对 RabbitMQ 为 ≤5s 陈旧的缓存值（其管理调用无同步廉价形式），Redis/InMemory 为精确值。Redis 的精确性依赖 F39 起「ack 后 XDEL」语义（否则 XLEN 会随历史流量单调增长、积压名不副实）——改动 Redis 消费路径时须保持 ack+删除成对出现。
3. `workflow_active_steps` 现为 1/0 Histogram 且失败路径不记 0，不可作为活跃步数消费；若要真正观测在跑步数，需改为 UpDownCounter 语义并补终态减记（独立技术债）。
4. 未接分布式追踪/日志聚合联动（日志侧已有 Serilog → Seq，见部署附录）。
