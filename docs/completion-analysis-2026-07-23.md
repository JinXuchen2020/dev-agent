# 全量代码完成度分析 — Agent 编排平台（dev-agent）

> 分析日期：2026-07-23 ｜ 方法：规划文档 + 代码扫描 + 实测构建/测试（非仅看文档）
> 结论先行：**后端工程扎实、测试充分、可跑可用；但"Agent 真正做事"的三条腿（工具执行 / 代码沙箱 / 联网调研）目前是桩代码或尚未开工，作为"能落地的 agent 编排平台"只能算核心可用、行动层缺失。**

---

## 0. 实测健康度（Ground Truth）

| 维度 | 结果 |
|------|------|
| 后端构建 `dotnet build AgentPlatform.sln` | ✅ **0 错误 0 警告**，10 个项目全部编译 |
| 后端测试 `dotnet test` | ✅ **204 通过 / 0 失败**（含 5 个 Testcontainers 真·Postgres+Redis 集成测试）|
| 前端构建 `npm run build`（tsc + vite） | ✅ 编译通过，仅 chunk 体积告警（1.46MB，对应 backlog O6）|
| `NotImplementedException` | **全仓 0 处** |
| 有意义的 TODO/placeholder 注释 | **仅 1 处**（`StubModelClient`，有意的本地双测桩）|
| EF Core 迁移 | ✅ 9 个迁移 + 模型快照齐全、ID 连续、与模型一致 |

> 关键判断：**文档乐观，但代码实测同样健康**。这不是"只会写 README 的脚手架"。

---

## 1. 解决方案与项目清单（README 与实际不符，需校正）

README 写"8 个项目 + AgentPlatform.Workflow 预留"，实际 `.sln` 引用 **10 个项目**：

| 项目 | 角色 | 状态 |
|------|------|------|
| AgentPlatform.Domain | DDD 领域层（聚合/值对象/仓储接口） | ✅ 真实 |
| AgentPlatform.Application | CQRS 命令/查询、编排原语、安全、工具调度 | ✅ 真实 |
| AgentPlatform.Infrastructure | EF Core、迁移、PgVector/InMemory 向量库、TenantProvider、Auth、编排器、工具、作业 | ✅ 真实 |
| AgentPlatform.Api | ASP.NET Core Web API、JWT/API-Key、RBAC、限流、提示注入中间件、Scalar | ✅ 真实 |
| AgentPlatform.SpecFlowTests | BDD 验收（6 个 .feature） | ✅ 真实 |
| AgentPlatform.ArchitectureTests | DDD 分层规则测试 | ✅ 真实 |
| AgentPlatform.IntegrationTests | Testcontainers 集成测试 | ✅ 真实 |
| AgentPlatform.Application.Tests | 处理器/领域单测（82 例） | ✅ 真实 |
| AgentPlatform.Infrastructure.Tests | 基础设施单测（59 例） | ✅ 真实 |
| AgentPlatform.Api.Tests | API 契约测试（11 例） | ✅ 真实 |
| AgentPlatform.Web | 独立 Vite/React/TS SPA（**不在 .sln**，正常） | ✅ 真实 SPA |

**注意**：不存在独立的 `AgentPlatform.Workflow` 项目——工作流能力已内嵌进 Domain/Infrastructure（DAG 节点 + 边 + 拓扑执行），属"真实内嵌"，非缺失。README 此条为历史漂移，建议校正。

---

## 2. 各能力完成度（真实 vs 空心）

| 能力 | 判定 | 证据 |
|------|------|------|
| Agent 编排（顺序 / 协商 + Critic 收敛） | ✅ 真实 | `SequentialOrchestrator`（DAG 拓扑、重试、回滚、上下文构建）、`NegotiationOrchestrator`、`OrchestrationPrimitive` 门面 |
| 工作流状态机 / DAG | ✅ 真实 | `Workflow` 聚合持有 Node+Edge+IsDag；`WorkflowNodeRunner` + 步骤执行器（Agent/Critic/Knowledge）；旧 `IStateMachineEngine` 已废弃（DI 直接抛异常，有意死路）|
| 模型路由 / 降级 / 熔断 | ✅ 真实 | `ModelRouter` + `ICostController` + `IResiliencePipelineProvider`，全失败抛 `AllModelsFailedException` |
| RAG / 向量库 | ✅ 真实 | `PgVectorStore`（Npgsql+pgvector+SK 嵌入+租户隔离+minScore）；`InMemoryVectorStore` 回退；`WordWindowChunker`/`DocumentTextExtractor`（PDF/HTML/纯文本）|
| 知识库（入库/检索/会话挂载） | ✅ 真实 | `KnowledgeBase` 聚合 + 控制器 + 完整 CQRS + 会话-KB 关联命令 |
| 多租户 | ✅ 真实 | `TenantProvider`（JWT `tenant_id` → `X-Tenant-Id` → 默认）；向量层也带 `tenant_id` 隔离 |
| 认证（JWT/API-Key）+ RBAC | ✅ 真实 | 双方案 + `ApiKeyEncryptionService`（AES-GCM）+ 密钥轮换/过期作业；`[Authorize(Roles=...)]` 遍布 |
| 执行日志 / 运行时日志 / SSE 进度 | ✅ 真实 | `ExecutionLog` 聚合、控制器、清理作业、`ExecutionProgressBroadcaster` |
| **工具调用（Tool Calling）** | ❌ **执行层全空心** | 三个 `IToolExecutor` 实现 **全是桩**：`NativeToolExecutor`→"Executed natively"、`SkillPackageExecutor`→"Executed via SK Plugin"、`McpClient`→"Executed via MCP"，均不真正执行即返回成功。调度/注册框架真实，但**任何工具都不干活** |
| **代码沙箱（Code Agent）** | ❌ **桩** | `DockerCodeSandbox` 仅打日志 + 返回成功，**不启动容器、不跑代码**。接口已注册，实现为空 |
| **联网调研（Research Agent / SerpAPI）** | ❌ **未开工** | 代码库中无 SerpAPI/ResearchAgent 实现，属 Phase 6，进度 0% |

---

## 3. 前端（AgentPlatform.Web）完成度

- **技术栈**：React 19 + TS(strict) + Vite 8 + Antd 5 + `@xyflow/react`（DAG 画布）+ zustand + axios。
- **真实页面（约 29 个源文件）**：Dashboard、Agents、AgentConfigurations、AgentRoles、Workflows、**WorkflowCanvas（DAG 编辑器：节点/调色板/配置面板/变量监视）**、KnowledgeBases、Conversation(聊天+KB 挂载)、ExecutionLogs、ApiKeys、Login，含 `ProtectedRoute` 鉴权门与 `ErrorBoundary`。
- **构建**：`tsc + vite build` 通过，0 `any`、lint 净。
- **残留缺陷（来自 `features/backlog.md`，多已标 done 但仍有 P2/P3 开口）**：
  - B7：Dashboard 大量指标是**硬编码假数据**（"今日会话 248"等），与真实拉取值混排，误导运营。
  - B8：ApiKeys 页仍是 **Mock + 死按钮**（后端当前无 ApiKey REST 端点）。
  - B9/B10/B11/O5/O12 等：YAML 不展示、状态筛选大小写可能不匹配、无错误态、分页未接 `totalCount` 等。
  - O6：未拆包（单 chunk 1.46MB）。
  - **测试覆盖薄**：仅 2 个可见测试（StatusBadge 单元 + AgentsPage 契约），13 个页面几乎无单测。

---

## 4. 阶段性进度（与路线图对齐）

| 阶段 | 内容 | 文档态 | 实测态 |
|------|------|--------|--------|
| Phase 1 | 基础 MVP（路由/RAG/Tool/Tool Calling 框架/成本） | ✅ | ✅ 编译+测试通过；**Tool 执行空心** |
| Phase 2 | 多智能体（状态机→编排原语/Redis/AutoGen/ExecutionLog） | ✅ | ✅ |
| Phase 3 | 平台化（可视化编排/监控/自定义 AgentType） | ✅ | ✅（DAG 画布已落地）|
| Phase 4 | 知识接地（RAG 真接地/Critic fail-loud/DB 分页/tokenizer） | ✅ | ✅ |
| Phase 5 | 安全加固（JWT/API-Key/RBAC/真多租户/限流/注入防护/审计/AES-GCM） | ✅ | ✅ 通过质量门 |
| RAG 自主配置收尾 | PDF/HTML 入库 + 知识检索节点 + 放开 RBAC | ✅ | ✅（质量门 `rag-self-config-closure`，codebase-optimizer PASSED，202 测通过）|
| **Phase 6** | **前沿特性（Code Agent 沙箱 / Research Agent / 压测 / BDD 全量 / 简历）** | 📋 计划 0% | **未开工**；`DockerCodeSandbox` 桩、`ResearchAgent` 无、`SerpApi` 无 |

> 质量门 `.quality-gate.json` 末次记录为 `rag-self-config-closure`，`codebaseOptimizer: PASSED`，并**自陈遗留 "5 Stub 含 DockerCodeSandbox P0-blocking 留 Phase 6"**——与本次扫描一致。

---

## 5. 可用性结论（核心问题：作为 agent 编排平台，能否用？）

### ✅ 已经可用（且扎实）
- **RAG 接地多智能体对话**：建会话、发消息、知识库入库/检索/会话挂载、多租户隔离、认证+RBAC 全链路真实可跑。
- **可视化工作流编排**：DAG 画布（拖拽/连线/配置）、`PUT` 草稿更新、拓扑序执行、Critic 收敛、SSE 实时进度（已带 JWT）。
- **平台底座**：EF 迁移、成本/执行日志、限流、提示注入防护、密钥 AES-GCM 加密、审计。

### ❌ 当前不可用 / 不可信（行动层缺失）
1. **工具调用（致命）**：三个执行器全是桩，Agent 调用任何工具都返回"成功"但什么都没做。**这是 agent 平台的核心能力，目前等于没有**。
2. **代码执行（Code Agent）**：`DockerCodeSandbox` 是空壳，无法真正跑代码→调试→修复闭环。
3. **联网调研（Research Agent）**：尚未实现，无 SerpAPI 集成。
4. **前端行动面**：ApiKeys 页 Mock、Dashboard 假数据，部分页面缺错误态/分页。

### 判定
> **这是一个"编排 + 知识 + 多智能体对话 + 可视化工作流"内核已经成型、工程质量很高的平台；但"Agent 真正在外部世界执行动作"（工具/代码/搜索）这一层目前是桩或空白。**
> 若你的目标是"内部知识问答 + 多 Agent 协作 + 流程编排"——**可用且值得继续投入**。
> 若目标是"能自动调 API、跑代码、做联网调研的自主 Agent"——**当前不可用，必须先把 Phase 6 的三条桩/空白补实**。

---

## 6. 建议的下一步（按优先级）

| 优先级 | 事项 | 说明 |
|--------|------|------|
| **P0** | 补实 `IToolExecutor` 三实现（至少 `NativeToolExecutor` 真实调用本地/HTTP 工具） | 解锁"Agent 真正做事"，否则平台名不副实 |
| **P0** | `DockerCodeSandbox` 接 Docker.DotNet 真实容器执行 | 解锁 Code Agent 闭环（Phase 6 验收 1）|
| **P1** | Research Agent + SerpAPI 集成 | Phase 6 验收 2 |
| **P1** | 前端 ApiKeys 页真实化 + Dashboard 去假数据 | 消除误导，接真实端点 |
| **P2** | 性能压测达标（并发 5 工作流、P95<10s） | Phase 6 验收 3 |
| **P2** | 前端补单测 + 拆包 + 统一错误态/分页 | 提升生产就绪度 |
| **P3** | 竞品对标开放项（版本管理/触发器/发布为 API·MCP/模板市场/Trace 视图） | 平台级增值 |

---

## 附：文档漂移校正清单
- README "项目结构"表写 8 个项目 + `AgentPlatform.Workflow` 预留 → 实际 10 个，无独立 Workflow 项目（已内嵌）。
- README 阶段表未体现"RAG 自主配置收尾"这一笔（已落地并通过质量门）。
