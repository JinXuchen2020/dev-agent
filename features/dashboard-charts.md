# F18 · Dashboard 图表充实（运行分析看板）

> 状态：设计就绪，待实现  |  优先级：P1  |  风险：🟡 中（新增 analytics 端点 + 前端图表库 + 时间聚合查询）
> 相关：F15(i18n 文案)、F16(卡片布局，Dashbord 不在其表格列表范围内)

---

## §1 目标

把当前只有 4 个计数卡的 Dashboard（`DashboardPage.tsx:35-66`）升级为**运行分析看板**：
在保留核心 KPI 卡的前提下，新增**时间序列图表 + 分布图**，让运营方一眼看清
「执行量趋势 / 成功率 / 延迟 / Token 消耗 / 对话量 / 哪些工作流最忙」。

---

## §2 现状核验（基于真实代码）

- **当前 Dashboard**（`src/AgentPlatform.Web/src/pages/DashboardPage.tsx`）：仅 4 个 `<Statistic>` 卡
  —— Active Agents / Workflows / Successful / Failed，全部来自 4 次计数请求，**无任何图表、无时间维度**。
- **后端无 analytics API**：全仓 grep `analytics|stats|metrics|dashboard` 仅有
  `OpenTelemetry` 的 `/metrics`（Prometheus 抓取端点，`Program.cs:106`）——
  那是运维层指标，**未以 REST 形式供前端消费**。前端图表必须新增一个聚合查询端点。
- **可聚合的数据源（已确认字段）**：
  | 聚合 | 可用图表字段 | 文件 |
  |---|---|---|
  | `ExecutionLog` | `WorkflowId`/`WorkflowName`、`TenantId`、`Status`(Running/Completed/Failed/RolledBack)、`StartedAt`、`CompletedAt`、`TotalSteps`、子项 `Entries[].Duration`(TimeSpan) | `Domain/Aggregates/ExecutionLogs/ExecutionLog.cs` |
  | `Conversation` | `TenantId`、`Status`、`TotalTokenUsage`(Prompt+Completion)、`CreatedAt`、`WorkflowId?` | `Domain/Aggregates/Conversations/Conversation.cs` |
  | `Agent` | `Status`、`CreatedAt` | `Domain/Aggregates/Agents/Agent.cs` |
  | `Workflow` | `Status`、`CreatedAt` | `Domain/Aggregates/Workflows/Workflow.cs` |
  | `Message` | `TokenUsage`(Per-message)、`CreatedAt` | `Domain/Aggregates/Conversations/Message.cs` |
- **多租户**：`ExecutionLog`/`Conversation` 均为 `TenantId` 隔离，新端点用现有 `ITenantProvider` 做租户过滤（与既有列表端点一致）。
- **成本数据缺口**：`ICostController` 现为 server-wide `_todaySpent`（非持久、非按租户），
  没有「模型单价表」。因此 v1 **只做 Token 消耗图，不做 $ 成本图**（见决策 D1）。

---

## §3 竞品对标（Agent 编排 / LLMOps 工具）

| 工具 | Dashboard / Analytics 核心图表 | 本平台可对标项 |
|---|---|---|
| **Dify** | 日活对话数、总消息数、Token 用量(输入/输出)、**预估成本**、端到端反馈(👍/👎)、**平均响应时间**；支持 7/14/30 天时间范围；折线+柱状 | 对话量、Token 图、延迟图、时间范围选择器 |
| **LangSmith** | Trace 数量趋势、**延迟 p50/p95**、**Token 成本**、反馈评分、按 LLM 拆分 | 执行量趋势、延迟图、按工作流拆分 |
| **Flowise** | 总对话数、总消息数、**平均响应时间**、Token 用量、API 用量；**按 Chatflow 拆分** | 延迟、Token、按工作流拆分 |
| **n8n** | 执行次数趋势、**成功率**、执行耗时、**按工作流拆分** | 执行量、成功率、延迟、按工作流 |
| **Coze(扣子)** | PV/UV、对话轮次、留存、满意度 | 对话量（UV 暂不可得，无端用户概念） |
| **Langflow / CrewAI Studio** | 基础运行计数 | 现状已超 |

**共性高价值图表（所有竞品都有的）**：
1. 运行/执行量随时间（折线/面积）
2. 成功率（折线 %）
3. 延迟/响应时间（折线/柱状）
4. Token 消耗（折线，Dify/LangSmith/Coze 成本焦点）
5. 按 Agent/工作流拆分（柱状/饼，n8n/Flowise 特色）
6. 对话量随时间（Dify/Coze 核心）
7. 时间范围选择器（7/14/30 天，Dify 标配）

---

## §4 图表清单（v1 落地，均租户隔离）

**KPI 卡（保留并扩充）**：Active Agents · Active Workflows · 执行总数(区间) · 成功率(区间) ·
总 Token(区间) · 平均延迟(区间)。沿用 antd `<Statistic>`。

**图表（新增）**：
| # | 图表 | 类型 | 数据源 / 聚合 |
|---|---|---|---|
| C1 | 执行量趋势 | 面积图（按状态堆叠：成功/失败/进行中） | `ExecutionLog` 按 `StartedAt` 日桶 `Status` 计数 |
| C2 | 成功率趋势 | 折线图(%) | `Completed / Total` 按日 |
| C3 | 平均执行延迟 | 折线/柱状(ms) | `ExecutionLogEntry.Duration` 按日均值（或 `CompletedAt-StartedAt`） |
| C4 | Token 消耗趋势 | 折线(总量/日) | `Conversation.TotalTokenUsage.TotalTokens` 按 `CreatedAt` 日桶求和 |
| C5 | 对话量趋势 | 折线(条/日) | `Conversation` 按 `CreatedAt` 日桶计数 |
| C6 | 热门工作流 Top N | 横向柱状 | `ExecutionLog` 按 `WorkflowName` 计数降序取前 8 |
| C7（可选） | 执行状态分布 | 饼图 | `ExecutionLog` 区间内 `Status` 占比 |

**时间范围选择器**：7 / 14 / 30 天（默认 14 天），驱动所有图表与 KPI 重算。

---

## §5 架构

### 后端（新增，不触现有契约）
- **`GET /api/v1/analytics/summary?from=ISO&to=ISO`**（`AnalyticsController`，沿用 `Admin,Operator`? → 实际应**所有已认证租户用户可读**，与 Dashboard 现有可见性一致）
  - 返回单一 `DashboardSummaryDto`：**一次请求**覆盖全部图表，避免 N 次调用：
    ```csharp
    record DashboardSummaryDto(
        DateTime From, DateTime To,
        Kpis Kpis,                         // 6 个 KPI 值
        List<DayBucket> ExecutionsByDay,  // C1/C2: date, completed, failed, running, successRate
        List<DayBucket> TokenByDay,       // C4: date, totalTokens
        List<DayBucket> ConversationsByDay,// C5: date, count
        List<DayBucket> LatencyByDay,     // C3: date, avgMs
        List<NameCount> TopWorkflows);    // C6: workflowName, count
    ```
  - 租户过滤：`ITenantProvider.GetTenantId()`；`from/to` 缺省回退 14 天。
  - 聚合实现：`Application/Analytics/Queries/GetDashboardSummaryQuery` + Handler；
    Handler 用仓储取区间内原始行（租户过滤），**在应用层按日桶聚合**（租户数据量可接受；
    若后期量大再下沉 SQL `GROUP BY` 日期截断）。复用 `IExecutionLogRepository` / `IConversationRepository`。
  - 门禁：沿用 Dashboard 现有可见性——`[Authorize]`（已认证即可，非仅 Admin）。
- **单元测试**：mock 仓储返回固定行 → 断言日桶聚合正确（成功率=completed/total、Token 求和、TopWorkflows 降序截前 8）。

### 前端
- **图表库**：推荐 `@ant-design/plots`（antd 官方 G2 封装，视觉与 antd 5 一致）；
  备选 `recharts`（更轻）。新增为 devDependency。
- **`DashboardPage.tsx` 改造**：
  - 顶部 `RangePicker`（antd）或 segmented 7/14/30 天 → 驱动 `getDashboardSummary(from,to)`。
  - KPI 卡区（现有 4 卡扩充为 6 卡）。
  - 图表区：`Row/Col` 网格排布 C1–C6（C1/C4/C5 占 12 列宽，C2/C3 占 12 列，C6 占 8~12 列）。
  - loading：`Spin` 或骨架；error：复用 `ErrorState`。
  - 标签用 `t()`（与 F15 协同，决策 D2）。
- **`services/api.ts`**：加 `getDashboardSummary(from, to)` → `api.get<DashboardSummaryDto>('/analytics/summary', {params})`。

### 无 EF 迁移
纯查询端点，不改 schema。

---

## §6 验收
- `GET /analytics/summary` 返回结构正确；租户 A 看不到租户 B 数据（隔离单测）。
- 前端 7/14/30 天切换 → 所有图表与 KPI 同步刷新；空数据期显示空态不报错。
- KPI 值与现有 4 卡口径一致（Agents/Workflows 计数不变）。
- 单测覆盖日桶聚合（成功率、Token 求和、TopWorkflows 截前 8）。
- tsc 0 / vitest 通过 / vite build 通过。

---

## §7 决策（待用户拍板）
- **D1 Token vs 成本**：v1 只做 **Token 消耗图**（无模型单价表，无法算 $）。若要做成本图，需先建「模型单价配置」（可作为 F13 延伸或独立小 feature）。
- **D2 i18n 协同**：图表轴/图例标签用 `t()`；可与 F15 同批或先硬编码中文。
- **D3 可见性**：Dashboard 摘要端点沿用现有「已认证即可读」（非仅 Admin），与当前页面一致；若你希望仅 Admin 看分析，改 `[Authorize(Roles="Admin,Operator")]`。
- **D4 图表库**：默认 `@ant-design/plots`；若在意包体用 `recharts`。

---

## §8 风险
- 时间聚合在应用层做，超大数据租户可能慢 → 已留 SQL `GROUP BY` 下沉余地。
- 新图表库增加前端包体（@ant-design/plots ~ 较大）；recharts 备选。
- 与 F16 卡片化不冲突（Dashboard 非表格列表页）；与 F15 i18n 协同即可。
