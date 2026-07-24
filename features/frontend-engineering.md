# F4 · 前端工程化（性能 / 可维护性 / 可访问性）

> 史诗 id：F4　|　优先级：P2/P3　|　类型：纯前端（无后端契约变更）
> 分支：`feat/f4-frontend-engineering`
> 来源：`features/backlog.md` F4 史诗（O6 / O9 / O10 / O14 / O7）

## 1. 目标

在不改动任何后端契约、不引入新路由/鉴权结构的前提下，提升前端的**性能（拆包）、可维护性（去静态 message / 清死代码）、可访问性（语义标签 + aria-label）与测试覆盖（关键页单测）**。延续 F1–F3 已奠定的数据真实性与鉴权态一致性，把工程化短板补齐。

## 2. 范围与边界（硬约束）

- **纯前端，无后端契约变更**：不新增/修改任何 API 端点、DTO、鉴权角色、路由结构。`.quality-gate.json` 随本 feature 前端改动一起提交。
- **拆包策略**：仅做「路由级 `React.lazy` + `manualChunks` 供应商分包」，不引入新的构建插件或运行时依赖（如 React Router 数据路由、MFSU 等），避免范围蔓延。
- **不借机重构无关代码**；O10 的「编辑器节点不可编辑/删除」经核实**已满足**（见 §5），本 feature 仅补最小交互外的清理。
- 复用既有设计令牌（`colors` / `Card` / `PageHeader` / `StatusBadge`）与 `AntApp`（F2 已把 `App` 包进 `App.tsx`）。

## 3. 现状核对（F1–F3 已修复，本 feature 不重复做）

| 项 | 现状 | F4 动作 |
| --- | --- | --- |
| 导航 a11y | `AppLayout` 已用 antd `Menu`（键盘可达），非 `div onClick` | 仅给侧栏折叠按钮补 `aria-label` |
| 静态搜索框 | `ConversationsPage` 的 `Input.Search` 已接 `onSearch` → `setAppliedQ` → 服务端重拉 | 仅补 `aria-label` |
| 编辑器节点编辑/删除 | `NodeConfigPanel` 可编辑各类节点配置；`删除节点` 按钮 + `ReactFlow` `deleteKeyCode={['Backspace','Delete']}` 已可删 | 已满足，验证通过，无代码改动 |
| 静态 `message` | `AppLayout` / `WorkflowsPage` 已用 `App.useApp()` | 其余 8 个页面改为 `App.useApp()` |

## 4. 改动清单（按验收子项）

### O6 · 路由级拆包（manualChunks + React.lazy）
- `vite.config.ts` 增加 `build.rollupOptions.output.manualChunks`：`react-vendor`(react/react-dom/react-router-dom)、`antd`(antd/@ant-design/icons)、`xyflow`(@xyflow/react) 三块供应商分包。
- `src/App.tsx`：所有页面（含 `LoginPage` / `NotFoundPage`）改为 `React.lazy(() => import(...))`；`<Routes>` 外包 `<Suspense fallback={<Spin size="large" />}>`。壳层 `AppLayout` / `ProtectedRoute` / `ErrorBoundary` 保持 eager。

### O9 · 静态 `message` → `App.useApp()`（8 个页面）
逐文件：从 antd import 移除 `message`，改 `import { ... App as AntApp } from 'antd'`，组件顶部 `const { message } = AntApp.useApp();`。
涉及：`LoginPage` / `WorkflowCanvasPage` / `ApiKeysPage` / `ConversationDetailPage` / `ConversationsPage` / `KnowledgeBaseDetailPage` / `KnowledgeBasesPage`（共 7 个；`WorkflowsPage`/`AppLayout` 在 F3 已完成）。

### O10 · 死代码清理（已核实）
- `stores/appStore.ts`：`userRole` 字段**仅被写入、从未被读取**（grep 全仓确认），属死代码 → 从 `AppState` 接口与 4 处 `set(...)` 中移除。保留 `authBootstrapped`（ProtectedRoute 读取）。
- 编辑器节点编辑/删除：已满足（见 §3），本 feature 不改动。

### O14 · 可访问性补强
- `AppLayout.tsx` 侧栏折叠按钮：加 `aria-label={sidebarCollapsed ? '展开侧边栏' : '收起侧边栏'}`。
- `ConversationsPage.tsx` `Input.Search`：加 `aria-label="搜索会话"`。
- `ConversationDetailPage.tsx` 聊天输入框 `Input.TextArea`：加 `aria-label="输入消息"`。
- 画布节点面板为拖拽式 `div`（已知键盘不可达局限），属 DAG 画布交互范畴，超出 F4 工程化范围，记录为已知限制，不在此修。

### O7 · 关键页单测（vitest + @testing-library/react）
新增（沿用 `vitest.config.ts` / `src/test/setup.ts` 现有桩）：
- `src/stores/__tests__/appStore.test.ts` —— 鉴权态迁移：`bootstrapAuth` 成功/失败、`loginReal`/`loginDemo`/`logout` 后 `isAuthenticated`/`isDemo`/`userEmail` 等断言。
- `src/hooks/__tests__/useApiState.test.ts` —— `loading`/`error`/`data`/`retry`：loader resolve/reject、卸载后不再 setState、retry 重跑。
- `src/pages/__tests__/LoginPage.test.tsx` —— 渲染 + 「演示会话」点击后 `loginDemo` 生效（用 antd `<App>` 包裹提供 message 上下文 + `MemoryRouter`）。
- `src/pages/__tests__/NotFoundPage.test.tsx` —— 渲染 404 文案与「返回首页」按钮。

## 5. 验收子项实现方案（与 §4 对应）

- **O6** `vite build` 产出多个 chunk（`xyflow`/`antd`/`react-vendor` 独立），首屏主包体积下降；路由切换按需加载页面 chunk。
- **O9** 全部页面改用 `App.useApp()`，消除 antd 静态 `message` 的 context 丢失告警；`tsc` 0 error。
- **O10** `userRole` 死字段移除；编辑器节点编辑/删除已验证可用（不重复实现）。
- **O14** 三个交互元素补 `aria-label`；键盘可达性不退化。
- **O7** 新增 4 个单测文件，覆盖鉴权态 / 异步加载 / 登录 / 404；`vitest run` 全绿。

## 6. 质量门禁清单（ddd-phase-quality-gate 嵌入项）

- **P0（阻断）**
  - [ ] `tsc --noEmit` 0 类型错误、0 `any`、0 未用导入（`verbatimModuleSyntax`/`noUnusedLocals` 严格）。
  - [ ] `vite build` 成功产出分包；`React.lazy` + `Suspense` 包裹正确，无运行期 `useApp` 上下文缺失。
  - [ ] 8 个页面 `message` 全部改为 `App.useApp()`，无残留静态 `message` 调用。
- **P1（高）**
  - [ ] `userRole` 死字段完整移除（接口 + 4 处赋值），无编译错误。
  - [ ] 新增 4 个单测文件，`vitest run` 全绿；覆盖鉴权态 / 异步错误态 / 登录 / 404。
  - [ ] `eslint` 0 error。
- **P2（中）**
  - [ ] a11y：侧栏折叠按钮 / 会话搜索 / 聊天输入框 三处补 `aria-label`，无遗漏。
  - [ ] 复用既有设计令牌与 `AntApp`，无新硬编码色值 / 新依赖。
- **P3（低）**
  - [ ] 无死代码、无未用导入；`lint` 净。
  - [ ] 画布节点键盘不可达记为已知限制，未在本 feature 范围内改动。

## 7. 风险与回归

- **回归面**：拆包后首屏资源变多（并行加载），但单文件更小；e2e 路由导航需等待 chunk 加载（Playwright `waitFor` 文本即可）。
- **无后端改动**：不触发后端质量门；本项目 e2e 依赖本地后端（port 5000）+ Web（5180），本沙箱无后端实例，故 QA 闭环跑 `typecheck/lint/build/unit` 四道闸门，`--e2e` 留待有后端环境执行。
- **`App.useApp()` 前提**：所有页面均在 `App.tsx` 的 `<AntApp>` 上下文内渲染，lazy 页面亦如此（Suspense 在 AntApp 之内），故调用合法。

## 8. 质量门禁记录

### 8.1 ddd-code-reviewer（对抗式审查，0 open）
- 控制流：`App.tsx` 的 `lazy` 页面均在 `<AntApp>` 上下文内（Suspense 位于 AntApp 之内），`App.useApp()` 调用合法；`useApiState` 的 effect deps = `[...deps, reload]`，不依赖 `loader` 标识，无每次渲染重复拉取/无限循环；`appStore` 移除 `userRole` 后全仓无残留读取（grep 0 命中）。
- `manualChunks` 函数式：在 Windows 环境 `id` 为 POSIX 风格（验证见 build 产物 `antd`/`react-vendor`/`xyflow` 三块独立 chunk），匹配正确，无 chunk 错配。
- 静态 `message.` 调用全仓 0 命中（grep 验证），8 个页面 + `WorkflowsPage` 均改 `App.useApp()`。
- 4 个新增单测覆盖鉴权态迁移 / 异步加载错误态 / retry / 卸载安全 / 登录 / 404，形态与既有 `AgentsPage.contract.test.tsx` 一致（mock api + assert 真实行为）。
- **结论**：详查 3 处最易出错点（lazy 上下文、useApiState 重跑、manualChunks 匹配），均无缺陷，无需修复。QA 四道闸门（typecheck/lint/build/unit）全绿。

### 8.2 ddd-phase-quality-gate（阶段结构门，PASS）
- 范围说明：本 feature 仅改动 `src/AgentPlatform.Web`（TypeScript/React）。ddd-phase-quality-gate 的 .NET DDD 类目（DI 注册、EF Core 映射、NuGet 版本、SpecFlow BDD、Swagger/OpenAPI、后端 CORS/Health/ProblemDetails）对纯前端改动 **不适用（N/A）**；以下按前端可适用类目 + 嵌入 §6 清单核对。
- §6 清单逐项核对：
  - **P0**：`tsc --noEmit` 0 错误 ✓（QA typecheck PASS）；`vite build` 成功产出分包 + Suspense 包裹 ✓（build PASS，产物含 antd/react-vendor/xyflow 独立 chunk）；8 页面 `message` 全改 `App.useApp()` ✓（grep 全仓 0 处静态 `message.`，9 处 `useApp`）。
  - **P1**：`userRole` 死字段完整移除（接口 + 5 处赋值）✓（grep 0 命中）；新增 4 单测文件 `vitest run` 全绿 ✓（QA unit PASS）；`eslint` 0 error ✓（QA lint PASS）。
  - **P2**：3 处 `aria-label`（侧栏折叠 / 会话搜索 / 聊天输入）已加 ✓；复用既有 `colors` 令牌与 `AntApp`，无新硬编码色值 ✓。
  - **P3**：无死代码（`userRole` 已清；`tsc` `noUnusedLocals` 强制）；`lint` 净 ✓。
- 交叉类目（前端可适用）：死代码（已清 userRole）、硬编码值（LoginPage 等沿用 `theme/tokens`）、并发（zustand store 不可变更新，无共享可变单例泄漏）、空值守卫（TS `strict` + 既有 `?.` 防御）。均无 open。
- **Gate Status: PASS  [P0:0 | P1:0 | P2:0 | P3:0]**
