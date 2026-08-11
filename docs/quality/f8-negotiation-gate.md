# F8 · 质量门禁报告（negotiation-productization）

- **Feature**：F8 · 差异化优势产品化（Negotiation + Critic）前端专属模式
- **分支**：`feat/f8-negotiation-productization`
- **日期**：2026-08-11
- **范围**：纯前端产品化；后端 `OrchestrationPreset.Negotiation` / `NegotiationOrchestrator` / `CriticStepExecutor` / `DetectPreset` 原语**零改动、零迁移、零契约变更**。
- **结论**：三道质量门 **cleared: true**（见根目录 `.quality-gate.json`）。

## 1. ddd-code-reviewer（对抗式审查）

**后端（.NET DDD）**：本次无任何后端代码改动（F8 仅前端）。按 feature-builder 约定，后端对抗审查 **0 open findings**（无新增/修改的 Domain/Application/Infrastructure/Api 代码可审）。

**前端（React19/TS）对抗审查**——聚焦本次改动 6 文件：

| 文件 | 审查点 | 结论 |
|---|---|---|
| `src/types/index.ts` | 新增 `OrchestrationPresetMode` 联合类型，注释明确「int 收发」契约 | PASS，无 `any` |
| `src/stores/workflowCanvasStore.ts` | `scaffoldAgentTeam()` 单一 `pushHistory` + 替换式构建 5 节点/4 边，严格按 `ValidateGraph`（单 Start、单 End、线性无环、名称唯一、从 Start 全连通） | PASS，支持撤销 |
| `src/services/api.ts` | `runExistingWorkflow(id, mode?)` 映射 `auto→省略 / sequential→{preset:0} / negotiation→{preset:1}`；int 枚举契约注释完整 | PASS，类型对齐 |
| `src/pages/WorkflowCanvasPage.tsx` | 引入 `Segmented`/`Tag`/`TeamOutlined`；`presetMode` state；`isNegotiationMode` 派生；`handleSaveAndRun` 透传 `presetMode`；工具栏加脚手架按钮 | PASS，未污染主路径 |
| `src/locales/zh-CN.ts` / `en-US.ts` | 新增 `canvas.preset*` / `negotiationMode` / `scaffoldAgentTeam*` 两语言对称 | PASS，无缺键 |

- 无 `any`、无 `TODO`/`FIXME`、无遗留 `console`。
- 无新建公共 API/聚合，不触碰鉴权/路由/契约（硬约束 §3 范围边界）。
- ESLint（改动文件）：**0 error，0 新增 warning**（仅 3 条 `react-hooks` warning 属既有 `useEffect` 代码，非本 feature 引入）。

## 2. ddd-phase-quality-gate（阶段结构门，P0–P3）

Checklist 已嵌入 `features/negotiation-productization.md` §6。核验：

- **P0（阻断）**：
  - [x] 预设以 int 收发（无字符串枚举误用）→ `api.ts` 显式映射 `0/1`，注释标注；`tsc` 类型对齐。
  - [x] 脚手架生成图通过 `ValidateGraph` → 单 Start / 单 End / 无环 / 全连通 / 名称唯一（Start/Architect/Developer/Critic/End）。
  - [x] 新建/保存运行不破坏既有工作流 → 替换式 scaffold 仍满足校验；既有 BDD 不受影响（后端零改动）。
- **P1（高）**：
  - [x] 协商模式指示在「含 Critic」与「显式选协商」两种条件下均正确（派生 `isNegotiationMode = presetMode==='negotiation' || nodes含Critic`）。
  - [x] `Segmented` 三态语义正确（auto=后端识别；sequential/negotiation→0/1）。
  - [x] i18n zh-CN / en-US 键对称。
- **P2（中）**：
  - [x] 脚手架按钮置于工具栏且显著，含 tooltip 说明。
  - [x] `scaffoldAgentTeam` 走单一 history 快照（支持撤销）。
  - [x] 代码无 `any`、无 TODO/FIXME、lint 净。
- **P3（低）**：
  - [x] 组件拆分合理，`preset` 逻辑未污染 `handleSaveAndRun` 主路径（仅在末尾透传）。
  - [x] 注释解释「int 枚举」契约缘由。

**P0/P1/P2/P3 = 0 open。**

## 3. codebase-optimizer（通用代码库优化）

F8 为最小化、低风险纯前端产品化，改动面小且自洽：

- 无新增 `stub`/占位实现——`scaffoldAgentTeam` 直接构建真实 DAG，`runExistingWorkflow` 直接映射真实 int 预设。
- 无生产就绪缺口——无硬编码密钥/连接串；沿用既有 `api` 实例与 antd 设计令牌。
- 复用充分：`STEP_TYPE_LABEL` / `defaultConfig` / `NODE_TYPE_TO_NODE_TYPE` 全部复用，无重复实现。
- 七维（架构/质量/正确性/测试/性能/安全/工程化）扫描：**0 open**（分析模式，不建分支、不 push）。

## 4. 自动化验证

| 验证 | 命令 | 结果 |
|---|---|---|
| 类型检查 | `tsc --noEmit` | 0 error |
| 生产构建 | `vite build` | 0 error（WorkflowCanvasPage 产出正常） |
| BDD 生成 | `bddgen` | 0 error |
| BDD 收集 | `playwright test --list` | 26 tests 含 F8 场景，0 未定义步骤 |
| 前端 Lint | `eslint`（改动文件） | 0 error |

> 说明：完整 `qa.mjs --e2e`（含浏览器 e2e）需本地后端 + dev server；F8 的 BDD 场景遵循既有「后端不可达整体 skip」约定，CI 有后端时实跑断言「脚手架→含 Critic→协商可见→保存运行→Completed」。前端单测/后端测试不因本 feature 引入回归（后端零改动）。

## 5. 残留风险

- **E2E 协商收敛确定性（低）**：Integration 环境 `ModelClient:StubResponse` 非 Critic JSON 且 `AllowCriticOverride=false` → Critic 判 Approved=false 后仍按「无可选步」路径完成（不无限循环）；E2E 仅断言 **Completed** 终态 + 无意外错误，不耦合具体评审结果（设计文档 §7）。
- **脚手架覆盖既有画布（低）**：`scaffoldAgentTeam` 采用替换式（保证单一 Start 通过 `ValidateGraph`），依赖单一 history 快照撤销；不静默丢弃未保存内容——符合「模板脚手架」语义。
