# F18 · Dashboard 图表充实（运行分析看板）质量门报告

**日期**：2026-07-30
**分支**：`feat/f18-dashboard-charts`
**门状态**：`PASS`（P0=0 / P1=0 / P2=0 / P3=0，全部闭环）

---

## 1. 实现概览

把仅 4 个计数卡的 Dashboard 升级为运行分析看板：后端新增单一聚合端点 `GET /api/v1/analytics/summary`（租户隔离、应用层按日桶聚合），前端引入 `recharts` 渲染 6 张时间序列/分布图 + 6 张 KPI 卡，含 7/14/30 天范围选择器。

### 后端（新增/改动）
- `AnalyticsController`（Api 层，`[Authorize]`，仅注入 `IMediator`）：`GET /analytics/summary?from=&to=`，透传 from/to，含 from>to→400、范围>366 天→400 边界。
- `GetDashboardSummaryQuery/Handler/DashboardSummaryDto`（Application 层）：拉取区间内租户原始行 → 日桶聚合（KPIs：ActiveAgents/ActiveWorkflows/TotalExecutions/SuccessRate/TotalTokens/AvgLatencyMs；ExecutionsByDay/TokenByDay/ConversationsByDay/LatencyByDay/TopWorkflows）。
- 仓储接口 `IExecutionLogRepository`/`IConversationRepository` 新增日期范围重载（Infrastructure 实现 `internal sealed`，`Include(Entries)` 保证延迟聚合）。
- 测试：`GetDashboardSummaryQueryHandlerTests`（6 例，含租户隔离/成功率/Token 求和/Top8）+ `EndpointContractTests` 集成契约（2 例：200 形状 + 倒序范围 400）。

### 前端（新增/改动）
- `types/index.ts`：新增 `DashboardSummary` 系列类型（camelCase 对齐后端）。
- `services/api.ts`：新增 `getDashboardSummary(from,to)`。
- `pages/DashboardPage.tsx`：重写——`Segmented` 7/14/30 天 + 6 KPI 卡（`Statistic`）+ 6 图（执行趋势堆叠柱/成功率折线/Token 面积/会话量柱/平均延迟折线/Top 工作流横向柱），空态复用 `Empty`，错误复用 `ErrorState`。
- `locales/zh-CN.ts` + `en-US.ts`：新增 `pages.dashboard` 图表键，严格镜像（i18n 对称性测试通过）。

---

## 2. ddd-code-reviewer（对抗式审查）

**结论：穷尽分析，无 P0/P1/P2 缺陷。**

- 静默崩溃路径核查：`ExecutionLogRepository.GetByTenantAsync` 已 `Include(Entries)` → 延迟聚合有效；`Conversation.TotalTokenUsage` 构造即 `= new TokenUsage(0,0)` 非 null；`ExecutionLogEntry.Duration` 为 `TimeSpan` 值类型；`from>to` 由 Controller 兜底 400。
- 控制流：Controller → `IMediator.Send(GetDashboardSummaryQuery)` → Handler 取四仓储 + `ITenantProvider`，全程 `ct` 透传；租户过滤到位；无 N+1（单次拉取后内存聚合）。
- 测试覆盖：handler 6 例 + 集成 2 例，覆盖成功率/Token 求和/Top8/租户隔离/空区间/倒序边界。

---

## 3. ddd-phase-quality-gate（结构门审计，12 类全扫）

| 类别 | 结果 |
|------|------|
| DI 注册缺口 | PASS（无新增接口需注册；Handler 程序集扫描实证） |
| DDD 层违规 | PASS（各层归位） |
| EF 映射缺口 | PASS（无新实体/迁移） |
| 硬编码值 | P3×2 → 已修（`Take(8)`→`TopWorkflowsLimit`、`366`→`MaxRangeDays`） |
| 缺 CancellationToken | PASS |
| 缺修饰符（internal sealed） | PASS |
| 并发风险 | PASS |
| 缺 null 守卫 | PASS |
| API 基础设施 | PASS（[Authorize]/ProducesResponseType/全局异常） |
| 蓝图漂移 | PASS |
| 缺 XML 注释 | PASS（新公开成员均中文 XML，含两仓储重载） |
| Swagger/API 文档 | PASS |
| 死代码/空类 | PASS |

---

## 4. codebase-optimizer（七维聚焦健康检查，F18 改动范围）

- 桩代码：无（Handler 真实聚合、Controller 真实派发、仓储真实查询）。
- XSS：`DashboardPage` 仅 antd + recharts 声明式组件，无 `dangerouslySetInnerHTML`。
- `any` 泛滥：前端类型显式，无 `any`；`tsc --noEmit` 0 error。
- 未捕获 Promise：`useApiState` 统一处理；无悬浮 promise。
- React key / hook 依赖：`useApiState(..., [range])` 依赖正确；图表数据由 recharts 渲染，无手动 `.map` 缺 key。
- 未用导入/依赖：`tsc` strict + build 0 警告。
- 硬编码密钥：无。
- **结论：PASS（0 open）**。

---

## 5. 验证结果

- 后端 `dotnet build src/AgentPlatform.sln`：**0 警告 0 错误**。
- 后端 `dotnet test src/AgentPlatform.sln`：**270 passed / 0 failed**（SpecFlow 41 / Arch 6 / Application 96 / Infrastructure 102 / Api 20 / Integration 5）。
- 前端 `tsc --noEmit`：**0 error**；`vitest`：**38/38 green**（含 i18n 对称 4 项）。

---

## 6. 设计偏离

- 图表库选用 **recharts**（设计默认 `@ant-design/plots` 的文档备选 D4）：React 19 兼容更稳、包体更轻；6 图 + 6 KPI 卡与设计的图表集合完全一致。
- D2/D3 决策默认采用「标签 t() 化」「已认证即可读」，与既有 Dashboard 权限一致。

---

## 7. 质量门 JSON

`.quality-gate.json` 推进至 `f18-dashboard-charts`（cleared: true）。
