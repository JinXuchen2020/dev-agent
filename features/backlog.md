# 功能待办池（backlog）

> **位置与定位**：仓库根 `features/backlog.md`（与 `src` 平级）。本池同时收纳**前端与后端**的 feature 设计与实现意图；**新增 feature 须先将设计文档放入 `features/`，再进入实现**（见下方红线）。
>
> **两层级消费模型**：
> - **Tier 1 · Feature 史诗（F1–F8）** —— 每个 = 一份 `features/<id>.md` 设计文档 + 端到端实现，是 `feature-builder` 的取数单元（取最靠前 `open` 史诗）。
> - **Tier 2 · 验收子项** —— 原 B/O/P 条目的归并，挂在对应史诗下，既作该史诗的完成判据，也可被 `feature-dev` 单独取出做细粒度前端实现。
>
> **红线**：代理**绝不**自己发明需求。只实现这里列出的、或你当轮明确指令的功能。涉及接口契约 / 鉴权 / 路由结构 / 破坏性后端等高风险改动时，代理会停下问人，不自动改。

状态图例：`open`(待做) · `doing`(进行中) · `done`(已完成) · `blocked`(阻塞等待依赖)

代码基现状（2026-07-22 全量走查）：React 19 + Vite 8 + TS（strict）+ Antd 5 + @xyflow/react + zustand；`typecheck/lint/build/unit/e2e` 五道闸门当前全绿。优点：严格 TS、0 处 `any`、0 TODO/FIXME、lint 净。问题集中于「前端数据真实性 / 鉴权态 / 错误兜底 / 工程化」与「后端行动层（工具·代码·调研）空心」两类。

---

## Feature 史诗（Tier 1 —— feature-builder 消费单元）

> 每个史诗含：目标 + 验收子项（原 B/O/P 归并，保留 `文件:行号` 锚点）+ 优先级 + 风险 + 设计文档链接。验收子项里的前端细项可由 `feature-dev` 直接取做。

### F1 · 前端数据真实性 & 全局错误态  [P2]  done  （已于 2026-07-23 经 feature-builder 流水线端到端实现并提交 `96fa5ee`；设计文档 features/frontend-data-truth.md + 质量报告 docs/quality/frontend-data-truth-gate.md）
- 设计文档：`features/frontend-data-truth.md`（已建）
- 目标：UI 展示真实登录身份、失败有兜底，消灭静默吞错与无 404 白屏。纯前端、低风险，无后端契约变更。
- 验收子项：
  - **B7** Dashboard 假数据 —— ✅ **done（漂移校正）**：现走真实 `getAgents/getWorkflows/getExecutionLogs`，原行号已失效。
  - **B8 / ApiKeys 页真实化** —— 🔒 **blocked**：后端无公开 API Key 端点（GET/POST/DELETE /api-keys、复制密钥），属 Phase 6 后端范围；待端点就绪后独立 feature 实现，本次不做。
  - **O4** 真实用户身份上顶栏（从 JWT 解码 email/role；dev-login 令牌无 tenant_id 声明，故不展示 tenant）→ open（本次实现）。
  - **O5** 静默吞错 → 统一错误 Alert + 重试（受改：`DashboardPage`、`ExecutionLogDetailPage:50`）→ open（本次实现；复用 `useApiState` + `ErrorState`）。
  - **O1** 顶层 ErrorBoundary —— ✅ **done（漂移校正）**：`components/ErrorBoundary.tsx` 已在 `App.tsx:32` 挂载包裹全部路由。
  - **O11** 404 兜底（新增 `NotFoundPage` + `App.tsx` `*` catch-all）→ open（本次实现）。文档链接原引用已失效（前端无硬编码文档链接），聚焦 404。

### F2 · 登录与鉴权态一致性  [P1]  done  ✅（2026-07-23，分支 `feat/f2-login-auth-state`，commit 19af124）
- 设计文档：`features/auth-ux.md`（已建）
- 目标：登录凭证真实校验、401 不破坏 SPA、鉴权态前后一致。
- 验收子项（全勾）：
  - **B6** 真实密码校验（PBKDF2）+ `LoginPage` 密码框 → ✅ 已实现。
  - **O2** 401 派发 `auth:unauthorized` 事件 → App 路由层 `<Navigate>` 跳转 `/login`，无整页刷新 → ✅。
  - **O3** demo 会话隔离（`isDemo` 跳过 401 跳，本地占位身份）→ ✅。
  - **O8** httpOnly + SameSite Cookie 鉴权（去 localStorage + 顺带解 B2 SSE 鉴权）→ ✅。
  - **O8 衍生** CORS 去 AllowAnyOrigin 改 WithOrigins+AllowCredentials；`/auth/me` 替代前端 JWT 解码 → ✅。
- 三道质量门禁全 PASS（ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer）；质量报告 `docs/quality/f2-auth-gate.md`。
- 已知残留（非阻断）：多租户登录（P2 waiver，目标后续 feature）；`JwtSecretKey`/`AesEncryptionKey` dev 兜底值（生产须环境变量覆盖）；种子默认密码生产须改。

### F3 · 页面交互打磨  [P2]  open
- 设计文档：`features/page-polish.md`（待建）
- 目标：列表/筛选/表单交互正确且一致。
- 验收子项：
  - **B9** AgentConfigurations 的 YAML 从不展示（`AgentConfigurationsPage.tsx:11-17`）→ 详情抽屉展示 `yamlContent`（语法高亮可选）。
  - **B10** 状态筛选枚举可能大小写不匹配后端（`ExecutionLogsPage.tsx:56-61`）→ 前端建状态映射表，不裸传字面量。
  - **B11** Workflows「快速运行」无错误处理且可能建空工作流（`WorkflowsPage.tsx:27-33`）→ try/catch + 失败 toast + 空名 `message.warning`。
  - **Conversations 搜索/状态筛选**（复用 conversations 数据，交互对齐 Agents 页）→ open。
  - **O12** 列表分页与后端 `totalCount` 不一致（`ExecutionLogsPage.tsx:70-77`）→ 接入服务端分页（传 `skip/take` + 用 `totalCount`）。
  - **O13** 无请求取消（AbortController）/ 卸载后 setState 风险 → effect cleanup + `AbortController`。

### F4 · 前端工程化（性能/可维护性/可访问性）  [P2/P3]  open
- 设计文档：`features/frontend-engineering.md`（待建）
- 目标：拆包、去告警、清死代码、补单测、a11y。
- 验收子项：
  - **O6** 打包体积过大未拆包（单 chunk 1.38MB；`@xyflow/react`、antd 全进主包）→ 路由级 `React.lazy` + `manualChunks`。
  - **O9** antd 静态 `message` 警告/上下文丢失（LoginPage/AgentsPage 等）→ 改 `App.useApp()`。
  - **O10** 死代码/未用能力；编辑器节点不可编辑/删除（`appStore.ts:7-8,20,24`；`AppLayout.tsx:178-210`；`WorkflowEditorPage.tsx:78-88`）→ 删或补交互。
  - **O14** 可访问性薄弱（导航 `<div onClick>`、静态搜索框、缺 `aria-label`）→ 改 `<a>`/`<button>` + `aria-label`。
  - **O7** 单元测试覆盖极低（<5%）→ 补关键页单测（数据获取/错误态/表单/SSE 解析）。

### F5 · 行动层落地（Agent 真正能做事）  [P0]  open  🔴高风险（Phase 6 行动层）
- 设计文档：`features/action-layer.md`（待建）
- 目标：让 Agent 真正在外部世界执行动作——调工具、跑代码、检索外部知识，而非伪造成功。这是「agent 编排平台」成立的核心。
- 验收子项：
  - **A1** 工具调用执行层全空心（三个 `IToolExecutor`：`NativeToolExecutor`/`SkillPackageExecutor`/`McpClient` 均直接返回伪造成功；`DI.cs:286-288`）→ 至少 `NativeToolExecutor` 接真实执行；验收须含「调用后结果反映真实副作用」。
  - **A2** 代码沙箱为桩（`DockerCodeSandbox.cs:9-56`；`DI.cs:146`）→ 接 `Docker.DotNet` 真实容器执行（镜像/网络/资源限制/超时/输出回传）；验收须含「真实运行代码并回传 stdout/stderr」。
  - **节点全家桶·Tool/Code/Knowledge Retrieval**（见 F7 节点项联动）→ 真实执行器接通，非装饰节点。

### F6 · Research Agent（联网多步调研）  [P1]  open  ⚠️高风险（Phase 6）
- 设计文档：`features/research-agent.md`（待建）
- 目标：SK 集成 SerpAPI（或等价搜索 API），实现「开放问题 → 多步搜索 → 结构化报告」的调研 Agent；多步链真实串联、外部 API 真实调用（非伪造结果）。
- 风险：外部 API 密钥与限流；多步链上下文膨胀须走统一 `WorkflowContext` + 逐步摘要压缩（对齐蓝图附录 C）。
- 验收：多步搜索 → 结构化报告且外部 API 真实调用。

### F7 · 工作流平台化（program，可拆子史诗）  [P2/P3]  open  ⚠️高风险
- 设计文档：`features/workflow-platformization.md`（待建，含子史诗拆分；来源 `./competitive-roadmap.md` 对标 Dify/n8n/LangGraph/Coze/Flowise）
- 目标：把 DAG 画布 MVP 推向生产级平台能力。以下子项各自可独立成 `feature-builder` 任务：
  - 工作流版本管理 + 导入导出（快照/回滚/JSON）。
  - 节点全家桶：Code(JS/Py) / HTTP / Tool(接 `ToolDefinition`) / Knowledge Retrieval(选库+topK+重排) / Condition / Loop / Variable / Sub-workflow / Delay / User-Input(HITL 审批门)。
  - 触发器：Webhook / 定时(cron) / Chat。
  - 发布为 API / MCP Server（复用现有 API Key；后端已有 MCP `ToolDefinition` 基础）。
  - 模板市场 / 示例库（5–10 行业模板一键克隆）。
  - 执行 Trace / 评估视图（节点级耗时/token/IO；数据集回归，对标 LangSmith/Langfuse）。
  - 工作流调试器（变量监视 + 单步重跑 + 错误分支）。
  - 企业增强：多工作空间隔离与切换 / 用量仪表盘 / 工作流 diff。

### F8 · 差异化优势产品化（Negotiation + Critic）  [native]  open
- 设计文档：`features/negotiation-productization.md`（待建）
- 目标：后端已具备 Negotiation 协商式多智能体 + Critic 收敛原语，待产品化画布「Agent-Team / Negotiation」专属模式（多 Agent 节点 + Critic + 收敛终止条件）。
- 说明：**保留，勿稀释**——Dify/n8n 无此原生原语，是本平台差异化壁垒。

---

## 已完成归档（done）

> 已交付、留作追溯；不再进入排期。新完成项请从上方史诗移入此处。

### 功能缺陷 / 后端（原一~二节 done）
- **B1** 工作流编辑器「编辑模式」失效且保存即运行 → done（2026-07-22，`PUT /api/v1/workflows/{id}` + 拆分「保存草稿/运行」；`docs/quality/p0-workflow-update-gate.md`）
- **B2** 实时进度 SSE 无法携带 JWT → done（fetch+ReadableStream 带 Bearer；`p0-workflow-update-gate.md`）
- **B3** WorkflowDetail SSE 无限重连刷屏 → done（AbortController.abort + 非 2xx 返回；`p0-workflow-update-gate.md`）
- **B4** WorkflowDetail 解析 context 白屏 → done（try/catch 回退原始文本；`p0-workflow-update-gate.md`）
- **B5** 会话功能不可用 → done（2026-07-23 聊天页打通 + KB 联动；`docs/quality/conversation-kb-linkage-gate.md`；发消息 RBAC 放开为「所有已认证租户用户」）
- **R1** 知识库无入库通道（RAG 静默 no-op）→ done（2026-07-23 地基层；`docs/quality/rag-foundation-gate.md`）
- **R2** 向量检索无租户隔离 → done（`document_embeddings.tenant_id` + WHERE；`rag-foundation-gate.md`）
- **R3** RAG 部署强耦合 SQLite 触发 500 → done（条件注册 + InMemory 回退；`rag-foundation-gate.md`）
- **R4** 检索无相关性阈值 + 工作流 RAG 语义错位 → done（minScore；`rag-foundation-gate.md`）

### 竞品对标（原五节 done）
- **P0** 后端工作流更新端点 + 前端编辑链路修通 → done（2026-07-22；`docs/quality/p0-workflow-update-gate.md`；设计 `./put-workflow-design.md`）
- **P1** 可视化 DAG 画布 MVP → done（2026-07-23；`docs/quality/p1-dag-workflow-gate.md`；设计 `./dag-workflow-design.md`）

### feature-dev 意图池 done
- **P1** 补全 Agents 页「新建 Agent」死按钮 → done（`POST /api/v1/agents` + Modal 表单；QA 五道闸门全绿）
