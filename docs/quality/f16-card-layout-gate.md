# F16 · 列表统一改为卡片（Card）形式展示 — 质量门报告

> 分支：`feat/f16-card-layout`　|　日期：2026-07-29　|　类型：纯前端 UI 打磨（无后端契约变更）
> 三道质量门：`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer` —— 全部 **PASS**（P0/P1/P2/P3 = 0 open）

---

## 1. 范围与产出

把 9 个「实体列表页」的 `<Table>` 渲染替换为响应式卡片网格，统一视觉与交互。

**新增基件**
- `src/AgentPlatform.Web/src/components/EntityCardGrid.tsx` —— 通用卡片网格：网格 + `Skeleton` 加载骨架 + `Empty` 空态 + 响应式列（`normal` 大屏 4 列 `lg=6` / `compact` 大屏 3 列 `lg=8`）+ `onItemClick` + `rowKey` + `density`。
- `src/AgentPlatform.Web/src/components/__tests__/EntityCardGrid.test.tsx` —— 7 项单测（渲染/空态/自定义空态/骨架/点击回调/交互子元素冒泡拦截/无 onItemClick 不挂点击）。

**改造页（9 个）**
| 页面 / 组件 | 渲染层改动 | 关键字段保留 |
|---|---|---|
| `AgentsPage.tsx` | `<Table>` → `EntityCardGrid` | 名称/角色/模型/创建时间/系统提示/状态 Tag + admin 编辑删除 `Popconfirm` |
| `AgentConfigurationsPage.tsx` | configsTab `<Table>` → `EntityCardGrid` + 下方 `Pagination` | 名称/类型/版本/启用 Tag/创建时间 + 点击开抽屉 |
| `WorkflowsPage.tsx` | `<Table>` → `EntityCardGrid` + 下方 `Pagination` | 名称/状态 Tag/步骤数/创建/更新 + 点击进详情 |
| `ConversationsPage.tsx` | `<Table>` → `EntityCardGrid` | ID/知识库 Tag/消息数/状态/开始时间 + 点击进详情 |
| `KnowledgeBasesPage.tsx` | `<Table>` → `EntityCardGrid` | 名称/集合/embedding Tag/文档数/创建时间 + 查看/删除(按钮 `stopPropagation`) |
| `CredentialManager.tsx` | `<Table>` → `EntityCardGrid` | 名称/供应商/模型/掩码/启用 Tag + 编辑/删除 |
| `ApiKeysPage.tsx` | `<Table>` → `EntityCardGrid` | 名称/前缀/角色/过期/最近使用/状态 + 轮换/吊销 |
| `ExecutionLogsPage.tsx` | `<Table>` → `EntityCardGrid`(`density=compact`) + 下方 `Pagination` | 工作流名/状态 Tag/总步骤/完成失败/起止时间 + 点击进详情 |
| `AgentRolesPage.tsx` | 两个 `<Table>` → 两个 `EntityCardGrid`（内置/自定义） | 名称/角色码/描述/系统提示/内建自定义 Tag |

**故意排除**
- `ResearchPage.tsx`：任务流（Timeline + 来源子列表），非实体列表，保持旧形态。
- 详情内子表（`ExecutionLogDetail` step entries / `KnowledgeBaseDetail` 文档列表 / `WorkflowDetail` Steps）—— 按 D2 保留 `<Table>`。

---

## 2. 三道质量门结论

### 2.1 ddd-code-reviewer —— PASSED
- **字段等价性**：逐页 `git diff` 核验原 `columns` 数据列全部平移为卡片元信息，**无丢失**（标题 / 状态 / 时间 / owner / 操作均保留）。
- **P0 修复 · 点击冒泡冲突**：`EntityCardGrid` 原整卡 `onClick` 会吞掉卡内交互子元素（按钮/链接/输入）的点击，导致「点删除→又触发整卡导航」双重动作。`KnowledgeBasesPage` 此前只能靠手动 `stopPropagation()` 自救，组件不安全默认。修复为安全默认：`handleItemClick` 用 `e.target.closest('button, a, input, select, textarea, [role="button"], [data-no-card-click]')` 命中即拦截整卡跳转；各页无需再手动 `stopPropagation()`。新增单测覆盖该行为。

### 2.2 ddd-phase-quality-gate —— PASS
适配前端语境的 8 类结构门逐项核验（清单嵌入 `features/card-layout.md §7`）：
1. Pre-flight Version Audit（N/A，无新增依赖）
2. BDD Scenarios First（N/A .NET；前端以 `AgentsPage.contract.test.tsx` + `EntityCardGrid.test.tsx` 7 项覆盖）
3. DDD Layer Rules（N/A，无后端层改动）
4. DI Registration Completeness（N/A，无新增后端接口）
5. **Configuration-First** → 卡片内全部用户可见文案走 `t()`，复用 F15 命名空间（`empty.*` 等键已确认存在），无硬编码用户串（逐页 grep 验证：中文仅出现在注释）。
6. EF Core Mapping Sync（N/A，无聚合/迁移变更）
7. **Concurrency & Lifecycle** → 列表页 `AbortController` 在 `useEffect` cleanup `abort()` 已核验；`EntityCardGrid` 无模块级可变状态；`onItemClick` 冒泡拦截（见 2.1）。
8. **Cross-Cutting Infrastructure** → i18n 一致、空态 `Empty`+`emptyText`、加载态 `Skeleton`、响应式列（`normal lg=6` / `compact lg=8`）、`rowKey` 全面覆盖、分页（`Pagination`）在后端分页页保留且筛选切换复位 `page=1`。

### 2.3 codebase-optimizer —— PASSED（前端专项）
- 无 `any` 类型引入；无 `dangerouslySetInnerHTML`；无新增硬编码中文 UI 串（渲染函数全 `t()`）；无残留 `Table`/`ColumnsType`/`Spin` 导入（F16 文件）；无未用导入（`colors` 均被引用）。
- 2 处 `console.error` 仅在 catch 块（既有错误日志，非死代码）。
- 无桩代码、无 XSS、无未捕获 Promise、`strict tsc 0 error`。

---

## 3. 验证结果

| 闸门 | 命令 | 结果 |
|---|---|---|
| 类型 | `tsc --noEmit` | **0 error**（strict） |
| 单元 | `vitest run` | **38/38 passed**（11 文件；含新增 EntityCardGrid 7 项 + AgentsPage 契约更新） |
| 构建 | `vite build` | **通过**（built in ~1.96s） |

模型一致性：无后端契约变更；纯前端渲染层改造。

---

## 4. 文档同步
- `features/backlog.md`：F16 `doing` → `done`（含完成记录）。
- `features/card-layout.md`：§5 决策 D1–D4 锁定；§7 嵌入三道门清单。
- `CHANGELOG.md`：顶部新增 v2.9 F16 条目。
- `README.md`：功能清单 F16 标记 ✅ 已完成。
- `AGENT_PLATFORM_BLUEPRINT.md` / `appendices/`：无列表渲染相关描述，无漂移，未改动。

## 5. 已知残留（非阻断）
- 详情内子表（D2，不在 v1）—— 保留 `<Table>`。
- `ResearchPage` 任务流（非实体列表，故意排除）—— 沿用旧形态。
- 与后续 feature 耦合：`AgentConfigurationsPage` 与 F17（实例化联动）、`AgentRolesPage` 与 F19（内建标记 + 页面补全）强耦合，F16 不改其写路径，由 F17/F19 收口。
