# 07. 项目演进：Phase 1 → 6 的设计思路

> 目标：理解为什么阶段是这个顺序，每个阶段解决什么问题，不做什么事。

> **一句话**：Phase 1→6 的顺序是「先骨架、再逻辑、后 UI/监控、补接地、补安全、做亮点」，每步都为下一步铺路。

---

## 7.1 阶段性概览

| Phase | 名称 | 定位 | 关键内容 |
|-------|------|------|----------|
| Phase 1 | 基础 MVP | 骨架 + 抽象（全 Stub） | 6 项目脚手架、DDD 分层、模型路由、SpecFlow BDD |
| Phase 2 | 多智能体工作流 | 真实业务逻辑 | 状态机引擎、Redis 缓存、AutoGen Agent、真实 PGVector、ExecutionLog |
| Phase 3 | 平台化 | 前端 + 监控 | React Web UI、Grafana 大盘、React Flow、OpenTelemetry、CI/CD |
| Phase 4 | 知识接地与加固（上线前必做） | 把声称完成实为存根的能力落地 | RAG 接真 PGVector、Critic fail-loud、DB 端分页、真 tokenizer 压缩 |
| Phase 5 ✅ | 安全加固（launch-blocking） | 把声称要做的认证/多租户落地 | JWT/API-Key 认证、RBAC、真实多租户、限流、提示注入防护、审计、API Key AES-256-GCM 加密 |
| Phase 6 | 前沿特性与收尾 | 优化 + 亮点 | Code Agent、Research Agent（✅F6）、行动层（✅F5）、性能压测、BDD 全量、简历作品集；F13 多租户凭据（安全后延伸，✅已完成） |

> **进度**：Phase 1~5 均已落地（Phase 5 于 2026-07-21 二次评审闭环 PASS，`dotnet test` 103/103）。Phase 6 进入实质实现期：**F5 行动层（2026-07-24 完成）**、**F6 Research Agent（2026-07-24 完成）**、**F13 多租户凭据配置（2026-07-27 完成，安全加固后的延伸史诗）** 均已交付；**F14–F19 设计就绪待实现**（见 `features/backlog.md`）。Phase 5 的知识点与排障详解见 `09`（加固）与 `10-phase5-security-learnings.md`（安全）。

## 7.2 为什么 Phase 1 全部用 Stub

### Phase 1 的目标不是"跑真实模型"，而是"验证架构能跑通"

```
Stub 组件清单：
┌────────────────────┬──────────────────────────┬──────────────────────────────┐
│ 组件               │ Stub 实现                │ 为什么可以 Stub               │
├────────────────────┼──────────────────────────┼──────────────────────────────┤
│ 模型调用           │ StubModelClient          │ 架构验证不需要真模型          │
│ 数据库             │ SQLite（代替 PostgreSQL）  │ 本地开发，不用启动 Docker      │
│ 缓存               │ InMemoryShortTermMemory   │ 一个 ConcurrentDictionary     │
│ 向量库             │ PgVectorStore（Phase 4 已接真 PGVector）│ Phase 1 为 Stub，Phase 4 落地真实向量检索 │
│ 工作流引擎         │ StubWorkflowEngine        │ Phase 2 才实现状态机           │
│ 代码沙箱           │ DockerCodeSandbox Stub    │ Phase 6 才需要真实沙箱         │
│ 工具执行器         │ NativeToolExecutor Stub   │ 返回常数字符串                │
│ Agent 编排        │ AutoGenAgentOrchestrator   │ Phase 2 才配置 AutoGen.NET    │
│ 用户认证           │ 跳过 JWT/Identity         │ Phase 5（安全加固）按蓝图实现  │
│ 通知 / 告警        │ 空实现                    │ Phase 3 监控才需要            │
└────────────────────┴──────────────────────────┴──────────────────────────────┘
```

**关键点：** 所有核心接口（`IModelClient`、`IVectorStore`、`IWorkflowEngine` 等）在 Phase 1 定义好。Stub 让接口有了消费者，Phase 2 替换成真实实现时，**立即可见接口设计是否合理**。

---

## 7.3 Phase 2 为什么是"多智能体工作流"

Blue 里写的是"多智能体"——但本质是**把 Phase 1 的 Stub 替换成能跑的真实逻辑**：

| Phase 1 | Phase 2 |
|---------|---------|
| `StubModelClient` | `IModelClient` 接真实 API（已有 `SemanticKernelModelClient`） |
| `PgVectorStore` | PGVector 真实向量检索（已在 Phase 4 落地） |
| `InMemoryShortTermMemory` | RedisShortTermMemory |
| `StubWorkflowEngine` | 自研状态机（分支/重试/回滚） |
| `AutoGenAgentOrchestrator Stub` | AutoGen.NET 真实协作 |
| — | ExecutionLog 持久化 + 查询 |

**为什么先做状态机再做 Agent？** 因为 Agent 协作需要状态机来做编排。如果先做 Agent，状态机还是 Stub，Agent 没法持久化中间状态。

---

## 7.4 Phase 3 为什么是"平台化"

Phase 2 跑通了核心逻辑，但**只有 API 没有 UI，只有日志没有监控**：

- React 前端：拖拽编排工作流、Agent 配置管理
- Grafana 大盘：模型延迟、工作流成功率、系统资源
- CI/CD：自动构建 + 测试 + 部署
- OpenTelemetry：Counter/Histogram 埋点

**为什么 Phase 3 才做前端？** 因为 Phase 2 之前 API 接口还在剧烈变化。先把后端接口稳定了，再写前端，避免前端频繁重写。

---

## 7.5 为什么 Phase 5 是"安全加固"（launch-blocking）

蓝图 §9 铁律写"安全是第一优先级，不是以后再补"，但实测落地时整层安全被遗漏、延到了本阶段——所以 Phase 5 不是"加功能"，而是把**早该有、声称有、实际没有**的安全底座补齐：

- **为什么单独成阶段、且 launch-blocking**：安全是运营前置门槛，不是亮点功能。埋进"前沿特性"会被持续排挤，所以独立成 Phase 5 并作为任何多用户/对外部署前的硬门槛。
- **为什么工作量可控**：多租户隔离的数据库层（EF Global Query Filter + `ITenantScoped`）早已建好，只差 `TenantProvider` 从硬编码 `DefaultTenantId` 改为按请求解析——属于"小而高杠杆"的改动，配上最小鉴权即可激活。
- **包含项**：JWT/API-Key 认证、RBAC、真实多租户、限流、Prompt 注入防护、审计日志、API Key 加密。内部上线若完整 Phase 5 未完，至少先落"最小 API-Key 网关 + TenantProvider 解析"兜底。
- **落地小结（2026-07-21）**：以上包含项全部真实接线并通过二次评审闭环。收尾时踩了三个"编译过、运行炸"的坑——认证无默认方案（`no DefaultChallengeScheme`）、Swagger 缺 Authorize 按钮、`EnsureCreated` 与迁移混用导致 `no such table`——印证了**安全代码"编译通过 ≠ 能跑"，接线后必须运行时实测**。详见 `10-phase5-security-learnings.md` §10.4。

---

## 7.6 Phase 6 为什么是"前沿特性"

最后阶段补齐"有亮点但非核心"的功能：

| 特性 | 为什么放最后 |
|------|-------------|
| Code Agent（生成-运行-调试-修复闭环） | 依赖 Phase 2 的沙箱 + Phase 3 的前端 |
| Research Agent（多步调研） | 依赖 Phase 2 的 Agent 协作 |
| 性能压测 | 前面的 Phase 没改完架构，压测结果会变 |
| BDD 全量验收 | 前面的功能还没写，测试自然没通过 |
| 简历文档 | 项目做完再整理 |

---

## 7.7 设计原则变化

| 原则 | Phase 1 | Phase 2 | Phase 3 | Phase 4（加固） | Phase 5（安全） | Phase 6（前沿） |
|------|---------|---------|---------|-----------------|-----------------|----------------|
| **测试策略** | Architecture + BDD | + Unit (状态机) | + Integration | + ddd-code-reviewer (RAG/Critic) | 安全模块 ddd-code-reviewer | + Stryker |
| **性能目标** | 不关心 | 不关心 | 基准：P95 < 30s | 复盘：DB 端分页 | — | 优化：P95 < 10s |
| **安全** | 跳过 | 基本 | 完整 | Critic 闸保真 | 认证/RBAC/真实多租户/限流/审计/Key 加密 | 沙箱逃逸防护 |
| **文档** | 蓝图 + 阶段文档 | + 学习文档 | + API 文档 | RAG 落地说明 | 安全设计说明 | + 简历作品集 |

---

## 7.8 如果你自己做项目，这个演进顺序值得借鉴

```
第一步：搭骨架（Phase 1）
  └─ 定义所有接口，全部 Stub，验证架构能跑通

第二步：填逻辑（Phase 2）
  └─ 替换 Stub 为真实实现，先做最核心的功能

第三步：加 UI + 监控（Phase 3）
  └─ API 稳定后再写前端，没有监控不上线

第四步：补接地（Phase 4）
  └─ 把声称完成实为存根的能力真正落地

第五步：补安全（Phase 5，launch-blocking）
  └─ 认证、真实多租户、RBAC、限流、审计、Key 加密

第六步：优化 + 亮点（Phase 6）
  └─ 性能、文档、CV 亮点
```

---

## 7.9 当前状态（2026-07-27 更新）

Phase 1~5 全部完成，Phase 6 进入实质实现期，并已向后延伸出安全加固后的「多租户凭据」史诗：

- **F5 行动层（2026-07-24 完成）**：`NativeToolExecutor` 真实 HTTP、`ProcessCodeSandbox` 真实进程、Tool/Code 工作流节点——Agent 真正能做事。
- **F6 Research Agent（2026-07-24 完成）**：SerpApi 真实联网多步调研 + SSE 流式。
- **F13 多租户凭据配置（2026-07-27 完成）**：补齐多租户化最后一环——外部 API 凭据层租户隔离（模型 LLM key + 搜索 SerpApi key 同构 BYO-Key + 平台内置回退 + 租户键控配额 + AES-256-GCM 加密）。详见 `features/model-config.md` 与 `docs/quality/f13-gate.md`。

**当前 backlog（设计就绪、待实现，详见 `features/backlog.md`）：**

| Feature | 优先级 | 一句话 |
|---------|--------|--------|
| F14 供应商模型发现 | P0 | 填 Key+BaseUrl 拉取 OpenAI 兼容 /models 模型清单 |
| F15 多语言 i18n | P1 | i18next + react-i18next，zh-CN/en-US 顶栏切换 |
| F16 列表改卡片 | P2 | EntityCardGrid 通用组件替代各页 `<Table>` |
| F17 AgentConfiguration 实例化（方案 A） | P2 | 把版本化 YAML 定义孤岛变为真模板库 + 消除重复凭据 tab |
| F18 Dashboard 图表 | P1 | analytics/summary 端点 + 6 KPI + C1–C6 图，对标 Dify/LangSmith/Flowise/n8n/Coze |
| F19 Agent Roles 内建+合并 | P1 | AgentRoleDefinition 加 IsBuiltIn + 合并 AgentType/AgentRoleDefinition 两套分类为「以 DB 为准」 |

**两项架构发现（驱动 F17/F19）：**

1. `AgentConfiguration` 是孤岛：运行时零引用（全仓 `AgentConfigurationId` = 0 处），只建了「库」没建「消费端」→ F17 补实例化链路（从定义实例化 Agent）。
2. 角色分类分裂：`AgentType`（architect/developer/tester/pm/tech-writer/reviewer，纯代码值对象）与 `AgentRoleDefinition`（architecture/development/testing/product/documentation/requirement，DB 聚合）**两套 code 完全不互通**；前端用硬编码 `BUILT_IN_ROLES` 判定内建却对不上 DB code → 系统架构等被误标「自定义」→ F19 加 `IsBuiltIn` 并把 `AgentType` 降为 DB 镜像（parity 测试强制对齐）。`RoleBasedSelectionStrategy` 只按 `WorkflowStep.StepName` 匹配、不读 `AgentType`，故合并角色 code 不影响路由（回归安全）。

---

## 复盘自测

- 为什么 Phase 1 全部用 Stub，而不是直接写实实现？
- 为什么 Phase 3 才做前端，而不是更早？
- Phase 5（安全）和 Phase 6（前沿）的本质区别是什么？

---

## 参考代码

- `AGENT_PLATFORM_BLUEPRINT.md` §5 — 阶段任务清单
- `phases/phase-1-baseline-mvp.md`
- `phases/phase-2-multi-agent.md`
- `phases/phase-3-platformization.md`
- `phases/phase-5-security-hardening.md`
- `phases/phase-6-frontier-features.md`
