# F40 · 异常回放诊断入口 质量门报告

> 日期：2026-09-03 · 分支 `feat/f40-replay-diagnostics`（基于 `feat/f39-observability-alerting`）· feature-builder 全栈流水线
> 设计文档：`features/f40-replay-diagnostics.md`（§3 能力边界、§6b 决策、§8 审查修复记录、Quality Gate Checklist）

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer | **PASS**（0 open） | P1×4 + P2×2 + P3 修复 |
| ddd-phase-quality-gate | **PASS**（P0=P1=0；P2×1 修，2×P3 waiver） | checklist 嵌入设计文档 |
| codebase-optimizer | **PASS**（Round F40-01，0 open） | P3×1 修（代理对安全截断），2×P3 waiver |

## 范围

后端只读诊断端点（`POST /api/v1/execution-logs/{id}/replay`）+ 前端回放视图（Tabs + `ReplayPanel`）+ **同批安全收口**（既有详情/steps 端点跨租户读取）。无 schema 变更、无迁移。

## ddd-code-reviewer 修复（关键项）

| 严重度 | 位置 | 问题 | 修复 |
|---|---|---|---|
| P1 | ReplayExecutionCommand | `TotalSteps` 在建档时未知恒为 0（`WorkflowStartedEventHandler:51` 明示，且聚合属性 `init` 不可变）→ `missingStepCount` **恒 0**，把「执行被截断」渲染成「无缺失」=假健康 | 新增 `total-steps-unregistered` 缺口码显式声明不可判 + 契约注释 + 测试锁定 |
| P1 | ReplayExecutionCommand | 无条目数上限：循环展开可产生无界节点，且每个节点 `input` 复制前序 `output` → 数十 MB 响应 | `MaxNodesInReport=500` / `MaxFailedStepNames=50` 封顶 + `report-nodes-capped` 码（失败统计仍全量，不牺牲判定正确性） |
| P1 | ReplayExecutionCommand | `TenantId==Guid.Empty` 回退 ambient 租户，但无回归锁 → 可无声退化回越权读 | 新增测试锁定「兜底 = 当前租户 fail-closed 且只走 `GetByIdForTenantAsync`」 |
| P1 | 既有详情/steps handler | 租户收口接线（本次改动）无任何 handler 级测试 | 新增 `ExecutionLogTenantQueryHandlerTests`（跨租户→null、无过滤路径零调用、非本租户不进 `QueryStepsAsync`） |
| P2 | ReplayPanel | `failedCount==0` 即渲染绿色「均为成功态」，把 Paused/RolledBack/执行中/空路径都误报健康 | 改三态判定（error / success / **info：信息不完整**）+ 回归用例 |
| P2 | ExecutionLogDetailPage | SSE 推进后回放报告陈旧不刷新 | 进度事件到达即失效已加载报告，切回 Tab 按需重取（错误态不自动重取，防请求风暴） |

## 结构门 / optimizer 补充

- 结构门 P2：Timeline 内 `Collapse` 以 `String(stepOrder)` 为 key，循环体展开会出现同序多条目 → key 冲突（activeKey 串档 + React 重复 key 告警）。改 `${stepOrder}-${index}`。
- optimizer P3：`Truncate` 按 UTF-16 code unit 直接切片会**撕裂代理对**，被截文本尾部的 emoji/生僻字会变成 U+FFFD —— 诊断信息被篡改。改为末位高代理时前退一位，`outputLength` 仍报原始长度，加代理对回归测试。
- Waiver（4 项，均已记录理由/风险/目标期）：截断与封顶上限为协议级 DoS 界（不走 IOptions，调大需重部署）；`errorTruncated`/节点时间戳/快照 `source` 等契约字段暂未在 UI 呈现（有值非蜜罐）；Entries 全量 `Include` 的物化读放大（最小修需改 Owned 投影并动已锁定的统计路径）；节点封顶后失败节点可能落在展示区外（已由 `report-nodes-capped` 诚实披露）。

## 诚实性校正（相对 backlog 原文，三条）

1. **每节点真实入参不落库**（Entry 只有 `Result`）→ 报告 `input` 为前序输出推断且 `inputInferred=true`；首节点直接返回 null，不用 `Workflow.Context` 的**当前值**冒充当时快照。
2. **F30 只有末次检查点**（覆盖写）→ 不声称可重建每一步上下文，`contextSnapshot.note` 明示边界并由前端转述。
3. **`TotalSteps` 恒 0** → 不假装能判断「执行被截断」（见上表 P1）。

## 安全收口（同批，用户决策）

`ExecutionLog` 未实现 `ITenantScoped`，全局 query filter 不覆盖；仓储 `GetByIdAsync` 也不带租户谓词 → 既有 `GET /{id}`、`GET /{id}/steps` 存在「持 GUID 读他租户日志」窗口（**F40 之前既有**）。新增 `GetByIdForTenantAsync` / `IsOwnedByTenantAsync`，三个读端点统一租户作用域（跨租户与不存在同为 404，不暴露存在性）；`ExecutionLogTenantScopeTests`（真 SQLite）实证「既有无过滤读取确实可跨租户取数」并锁定新路径，防回归。

## 验证

- 后端：`dotnet build AgentPlatform.sln` **0 警告 0 错误**；Application **285/285**、Infrastructure **175 + 8 跳过**、Api **39/39**、Architecture **9/9**、Integration **5/5**（需 `OPENAI__Key`）、SpecFlow **117/118**（唯一失败 = master 既有 LLM 用例）。
- 新增测试：Replay handler 14 例（只读性/跨租户/损坏检查点降级/截断代理对/封顶/缺口码）、租户收口 handler 测试、EF 收口测试、`ReplayPanel` 组件 8 例；后端 Reqnroll 2 场景（内容级断言，非仅状态码）；前端 playwright-bdd `execution-log-replay.feature` + `executionLog.steps.ts`（`bddgen` exit 0，CI 运行）。
- 前端：`tsc --noEmit` 0 error；`vitest` **50 通过**（2 处既有豁免）；`vite build` 通过。
- 集成种子新增确定失败日志（`FailedExecutionLogId` + 末次检查点），供 BDD/E2E 复用同一数据面，避免场景依赖运行时新执行。

## 已知残留（非阻断）

1. `errorTruncated`、节点时间戳、快照 `source` 等契约字段暂未在 UI 呈现（数据已在响应中）。
2. e2e 步骤内硬编码种子 GUID / 失败步名，靠注释与 `IntegrationConstants` 锚定（后续可代码生成消除）。
3. 只读诊断不写审计（与既有查询端点一致）；如需「谁看过哪次失败」需另立 feature。
