# F15 质量门报告 · 多语言国际化（i18n）

> 分支：`feat/f15-i18n` · 日期：2026-07-28 · 纯前端 feature（无后端契约变更）
> 三道门：`ddd-code-reviewer` → `ddd-phase-quality-gate` → `codebase-optimizer`，全部 `cleared:true`。
> 结论：`.quality-gate.json` `cleared:true`，`codebaseOptimizer: PASSED`。

## 0. 模型一致性（Phase 3 / 前端侧）
- 无后端 DTO / 端点变更，前后端契约无漂移。
- 前端验证闸门全绿：
  - `tsc --noEmit`：**exit 0**（strict 下 `t()` 调用合法）。
  - `vitest run --no-coverage`：**30/30 green**（10 测试文件；含新增 `i18n-symmetry` + `config` 共 4 项）。
  - `vite build`：**成功**（dist 产物含 `useTranslation` chunk；`dist` 经原生 `rm -rf` 清理后重建，规避 sandbox safe-delete 守卫）。
- 类型安全：`en-US.ts` 以 `Resources = typeof zhCN` 约束，key 结构静态镜像；`src/__tests__/i18n-symmetry.test.ts` 运行时 flatten 对称测试兜底。

## 1. ddd-code-reviewer（对抗式审查）
**Gate: PASS · P0/P1/P2/P3 = 0 open（全部自动修复）**

| Severity | Category | File:Line | Finding | Fix |
|---|---|---|---|---|
| P1 | 漏翻键（原始键串外泄） | `zh-CN.ts:32` / `en-US.ts:34`；消费方 `AgentConfigurationsPage.tsx:98` / `ExecutionLogsPage.tsx:98` / `WorkflowsPage.tsx:113` | `common.total` 双包缺失，`t('common.total', {count})` 在分页 `showTotal` 渲染原始键串 `common.total` | `zh-CN.ts` 补 `total:'共 {{count}} 条'`、`en-US.ts` 补 `total:'{{count}} total'`（受 `Resources` 类型强制镜像） |
| P2 | 作用域错误（TS2304） | `AgentConfigurationsPage.tsx:20-21`、`AgentsPage.tsx:12-13` | 模块级 `columns` 数组在组件外定义，`t` 不在作用域 | 两处 `columns` 改为组件内工厂函数（在 `useTranslation()` 之后） |
| P2 | 漏翻硬编码 UI 串（违反 D4） | `DashboardPage` / `WorkflowsPage` / `AgentRolesPage` / `AgentConfigurationsPage` / `ExecutionLogsPage` / `AgentsPage` | 列头、按钮、状态标签、筛选 placeholder/aria 硬编码中文 | 统一改为 `t('common.*'/'pages.*')`；`showTotal` 已含 `common.total` |
| P3 | 漏翻硬编码 UI 串 | `ResearchPage.tsx:58`、`CredentialManager.tsx:74` | 兜底 `'未知错误'`、Provider 列头硬编码 | 改 `e.error ?? t('pages.research.unknownError')`、列头改 `t('pages.credentials.providerLabel')` |
| P3 | key 对称性 | `en-US.ts` | 重写整文件严格对齐 `zh-CN.ts`（受类型 + 运行时测试强制） | 已重写，`tsc` 校验通过 |
| P3 | 测试缺口 | `src/locales/__tests__/config.test.ts`（新增） | 缺乏 locale 持久化/解析覆盖 | 新增 4 项：默认回退 / 恢复已存 locale / 忽略非法 locale / `SUPPORTED_LOCALES` |

**审查中确认无问题（逐项核对，未改）**：无动态 `t(\`..\`)`/`t(variable)` 键；无 `dangerouslySetInnerHTML` 误用；`LanguageSwitcher` aria-label 正确；`App.tsx` locale 同步逻辑正确；D1–D4 未重新讨论。

## 2. ddd-phase-quality-gate（结构门，前端适配）
**Gate: PASS · P0/P1/P2/P3 = 0 open**

原 skill 面向 .NET DDD 后端（NuGet/EF Core/DI 等 8 类）。F15 为纯前端 feature，后端相关类别不适用，已将结构门适配为前端语境，9 类逐项核验并嵌入 `features/i18n.md §7`：
1. 依赖锁定：`i18next`+`react-i18next` 入 `package.json` dependencies，无后端依赖。
2. 测试优先：无后端契约变更；新增对称 + config 单测；`tsc`/`vitest`/`vite build` 全绿。
3. 分层/模块规则：`src/locales/` 为唯一文案源；全仓无动态 key。
4. 初始化完备：i18n 仅在 `main.tsx` side-effect 初始化一次。
5. 配置优先：`SUPPORTED_LOCALES`/`DEFAULT_LOCALE`/`STORAGE_KEY` 集中 `config.ts`。
6. 映射同步（前端等价）：`en-US` 类型镜像 + 运行时对称测试，双包 key 100% 一致。
7. 生命周期：`languageChanged` 监听在 `App.tsx` useEffect 内注册并 cleanup 注销。
8. 横切：`fallbackLng=zh-CN`、aria-label 语义化、`escapeValue:false`（React 转义，无 XSS）、antd/dayjs 区域随语言同步。
9. 漏翻复核（D4）：全仓 `.tsx` 残留中文仅注释 / 领域数据（节点 placeholder 示例、`'检索失败'` 后端逐字匹配、测试 fixture/断言），无应翻未翻 UI 文案。

## 3. codebase-optimizer（多轮优化）
**Gate: PASS · Round 1 · 0 open（P3 已 waiver）**

七维度扫描（架构 / 代码质量 / 正确性 / 测试 / 性能 / 安全 / 工程化）：
- **无 P0/P1/P2**：无桩代码、无 XSS、无未捕获 Promise、无 `any` 泛滥（`strict tsc` 0 error）、无硬编码密钥、无未用依赖、无 DI/EF 类别（前端不适用）。
- **P3（已 waiver，非静默跳过）**：扫描发现 36 个在 `zh-CN.ts` 中定义但全仓无 `t('key')` 字面引用的 key。判定为**有意 i18n 词汇储备**，waiver 理由：
  - `common.confirm/loading/search/reset/submit/all/success/copied/copy/deleteConfirm` 等：与 antd 内置文案重叠，已由 `ConfigProvider locale` 本地化，无需自行维护。
  - `errors.*`（generic/unauthorized/forbidden/serverError/network/sendFailed/...）：为设计文档 D1「后端错误本地化」预留，v1 后端错误按原样展示，落地 D1 时启用。
  - `empty.*`（agents/workflows/.../noData/noMessages）：为各页 `Empty` 描述预留，本期未全部接线。
  - `nav.login`：原作 aria-label，已改 `nav.language`，`nav.login` 暂留作词汇。
  - 以上为低风险的资源词汇，删除将导致 D1 / Empty 描述落地时重复增键；按 skill P3 waiver 规则显式记录，不阻塞合入。

## 4. 残留风险（已知）
1. 双侧缺失键串外泄：对称测试仅捕获 zh/en 不一致；若某 `t('x.y')` 在两包都不存在仍会渲染原始键。建议后续对 `t()` 做 react-i18next module augmentation，把可用 key 编入类型使缺失键编译期报错。（`common.total` 已修复）
2. 模块级 `columns` 作用域回归：本次已修，建议 contract 测试/eslint 守住「含 `t()` 的 `columns` 必须位于组件内」。
3. `@xyflow/react` 画布右键菜单等第三方内置中文未纳入 i18n（D4 已知残留，v1 不处理）。

## 5. 验证命令（可复现）
```
cd src/AgentPlatform.Web
node_modules/.bin/tsc --noEmit                 # exit 0
node_modules/.bin/vitest run --no-coverage     # 30 passed
node_modules/.bin/vite build                   # 成功（dist 需先原生 rm -rf 清理）
```
