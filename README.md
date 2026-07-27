# Agent Platform

企业级、强类型、可维护的自研 Agent 编排平台，基于 .NET 9 + DDD + Clean Architecture。

## 快速开始（跳过 Docker）

```bash
cd src/AgentPlatform.Api

# QuickStart 模式：SQLite + Stub 模型，无需外部依赖
dotnet run --launch-profile QuickStart

# 浏览器打开 API 文档
open http://localhost:5000/scalar/v1

# 创建会话并发消息
CONV_ID=$(curl -s -X POST http://localhost:5000/api/v1/conversations \
  -H "Content-Type: application/json" | jq -r '.id')
curl -X POST "http://localhost:5000/api/v1/conversations/$CONV_ID/messages" \
  -H "Content-Type: application/json" \
  -d '{"content":"Hello","model": "stub"}'
```

## 项目结构

| 项目 | 职责 |
|------|------|
| `AgentPlatform.Domain` | 领域层 — 聚合根、值对象（`AgentType`）、仓储接口，零外部依赖 |
| `AgentPlatform.Application` | 应用层 — MediatR Command/Query、路由策略、状态机事件处理器、工具调度 |
| `AgentPlatform.Infrastructure` | 基础设施 — EF Core、Semantic Kernel、Redis 短期记忆、AutoGen 编排、状态机引擎、ExecutionLog |
| `AgentPlatform.Api` | 表现层 — ASP.NET Core Web API 含 Agents/AgentRoles/ExecutionLogs/Auth 端点、JWT（Cookie 承载）+ API-Key 认证（Smart policy scheme）、RBAC、限流、提示注入中间件、Swagger/Scalar、CORS |
| `AgentPlatform.Workflow` | 工作流引擎（预留） |
| `AgentPlatform.SpecFlowTests` | BDD 验收测试（SpecFlow + xUnit，11 个 .feature 文件） |

## 构建与测试

```bash
# 构建全部项目
dotnet build src/AgentPlatform.sln

# 运行全部测试
dotnet test src/AgentPlatform.sln
```

## 配置真实 API Key

```bash
dotnet user-secrets set "OpenAI:Key" "sk-your-key-here"
```

详见 [AGENT_PLATFORM_BLUEPRINT.md](./AGENT_PLATFORM_BLUEPRINT.md) §10.2。

## 架构要点

- **DDD 依赖方向**: Api → Application → Domain, Infrastructure → Application
- **MediatR 管道**: UnitOfWorkBehavior 自动管理事务和领域事件分发（仅 `ICommand<T>` 触发 SaveChanges）
- **模型路由**: 基于优先级列表的降级/重试/成本控制
- **多租户**: ITenantScoped + EF Core Global Query Filter（Phase 5 升级为 per-request 真实多租户；F13 新增外部 API 凭据层租户隔离——模型/搜索 BYO-Key + 平台内置回退）
- **认证**: httpOnly + SameSite Cookie 承载 JWT（前端 `withCredentials`，不落 localStorage）+ PBKDF2 密码哈希；API-Key 与 Bearer 并存于 Smart policy
- **状态机引擎**: 自研 `WorkflowStateMachineEngine`，支持分支/重试（可配置次数）/回滚
- **多 Agent 编排**: `AutoGenAgentOrchestrator` 顺序管线，6 种预置角色 + 自定义 `AgentType` 值对象
- **短期记忆**: Redis 实现 `IShortTermMemory`，`IConnectionMultiplexer` Singleton，连接失败降级到内存
- **运行时日志**: `ExecutionLog` 聚合根，5 个领域事件贯穿工作流生命周期
- **可插拔数据库**: 条件编译 `USE_SQLITE`/`USE_POSTGRESQL`，`DatabaseInitializer` 自动初始化
- **BDD**: SpecFlow Gherkin 验收用例驱动开发（41 个场景全部通过）

## 阶段路线

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 1 | 基础 MVP — 路由、RAG、Tool Calling、成本报表 | ✅ 完成 |
| Phase 2 | 多智能体工作流 — 状态机、Redis、AutoGen 编排、ExecutionLog | ✅ 完成 |
| Phase 3 | 平台化 — 可视化编排、监控、自定义 AgentType | ✅ 完成 |
| Phase 4 | 知识接地与加固 — RAG 真接地、Critic fail-loud、DB 分页、真 tokenizer | ✅ 完成 |
| Phase 5 | 安全加固（launch-blocking）— JWT/API-Key 认证 / RBAC / 真实多租户 / 限流 / 提示注入防护 / 审计 / API Key AES-256-GCM 加密 | ✅ 完成 |
| Phase 6 | 前沿特性 — Code Agent、压测、BDD 全量（F5 行动层 / F6 Research 已完成） | 🔄 进行中（F13 多租户凭据已完成；F14–F19 设计中） |

## 功能特性进度

最新功能规划与实现状态见 [`features/backlog.md`](./features/backlog.md)：

- **F13 多租户凭据配置** ✅ 已完成（2026-07-27，`feat/f13-multi-tenant-credentials`）
- **F14 供应商模型发现** / **F15 多语言 i18n** / **F16 列表改卡片** / **F17 AgentConfiguration 实例化** / **F18 Dashboard 图表** / **F19 Agent Roles 内建+合并** —— 设计就绪、待实现（各 feature 设计文档在 `features/` 目录）

> 约定：新增 feature 须先将设计文档放入 `features/`，再进入实现（见 backlog 红线）。

## 学习资料

本项目配套一套**通俗化学习笔记**，把各阶段的设计决策、踩坑与复盘拆成「导读 → 分章 → 速记卡」三层，方便日后复盘：

- 📚 **总入口**：[`docs/learning/00-学习导读.md`](./docs/learning/00-学习导读.md) — 阅读顺序、速查表、纠错记录
- 🗂️ **分章笔记**：
  - [`01-ddd-in-practice.md`](./docs/learning/01-ddd-in-practice.md) — DDD 实战
  - [`02-clean-architecture.md`](./docs/learning/02-clean-architecture.md) — 整洁架构
  - [`03-mediatr-cqrs.md`](./docs/learning/03-mediatr-cqrs.md) — MediatR / CQRS
  - [`04-ef-core-aggregates.md`](./docs/learning/04-ef-core-aggregates.md) — EF Core 聚合
  - [`05-testing-strategy.md`](./docs/learning/05-testing-strategy.md) — 测试策略
  - [`06-common-pitfalls.md`](./docs/learning/06-common-pitfalls.md) — 常见坑（含「按症状查因」表）
  - [`07-project-evolution.md`](./docs/learning/07-project-evolution.md) — 项目演进
  - [`08-decision-log.md`](./docs/learning/08-decision-log.md) — 决策日志
  - [`09-phase4-grounding-learnings.md`](./docs/learning/09-phase4-grounding-learnings.md) — Phase 4 知识接地（含「按能力查因」表）
  - [`10-phase5-security-learnings.md`](./docs/learning/10-phase5-security-learnings.md) — Phase 5 安全加固（认证/多租户/RBAC/Key 加密/审计 7 个知识点 + 3 个排障实录）
- 🃏 **速记卡**：[`docs/learning/cheatsheet-复盘速记.md`](./docs/learning/cheatsheet-复盘速记.md)（文字版）/ [`docs/learning/cheatsheet-复盘速记.png`](./docs/learning/cheatsheet-复盘速记.png)（图片版，可一键保存手机常看）

## 质量治理流程

本平台有**四道质量关**贯穿所有阶段，形成「动手前审范式 → 动手后审实现 → 结构卫生 → 全库健康/生产就绪」的闭环：

| 关 | 时机 | 审什么 | 负责 Skill | 规范出处 |
|----|------|--------|-----------|---------|
| **设计评审关** ⭐ | 动手写/改任何「蓝图能力」之前 | 蓝图范式对不对（线性瀑布 / 缺 critic 循环 / 上下文爆炸 / RAG 不接地 / HITL 无断点 / 恢复过度承诺） | `blueprint-architecture-review` | phase-1 §0-1 |
| **§0 路由策略** | 动手后，高风险模块合入前 | 代码有没有照蓝图做（「名不副实现」高风险区强制 reviewer） | `ddd-code-reviewer`（高风险）/ `ddd-phase-quality-gate`（结构） | 各 phase §0 |
| **结构门禁** | 各阶段 | DDD 卫生（DI / 分层 / EF / 并发 / 密封 / 守卫） | `ddd-phase-quality-gate` | 各 phase §0 |
| **全库健康检查** | **阶段完成时**（最后一笔提交前） | 8 维度全库扫描：架构 → 代码质量 → 正确性 → 测试 → 性能 → 安全 → 工程化 → **桩代码替换进度** → **生产就绪度** | `codebase-optimizer` | 本 README § 质量治理 / QUALITY-GATE.md |

> ⭐ `blueprint-architecture-review` 是**设计时（design-time）门禁**，在白板阶段启动时执行，结论为 **DESIGN READY** 后进入编码。它不是提交门禁，不进入 `.quality-gate.json` 标记体系。

**关键约定**
- 每个 Phase 只保留**一个**文件（`phase-N-<主题>.md`）；质量门禁清单**就地写入**该文档小节，不再单独生成 `phase-N-checklist.md`。
- 设计评审关结论须达 **DESIGN READY** 才许进入对应 Phase 编码：P0 项阻断、P1 项须在对应 Phase 的 `ddd-code-reviewer` 强制范围内闭环。
- `ddd-code-reviewer` 报告必须显式写出「已核对的蓝图章节」（如 "verified against 附录 C.6 / §8.2"），缺此项视为未通过。
- **责任边界**：漂移问题归「写代码的 Phase」（如编排器漂移归 Phase 2）；范式问题归「设计评审关」，Phase 3 不应替 Phase 2 背范式债。
- **桩代码替换进度归入 codebase-optimizer**：阶段完成时，`codebase-optimizer` 扫描蓝图 Stub 清单，逐项验证替换状态。未替换组件须评估生产影响，遗留须写入 final-summary 的"已知遗留问题"章节。

**提交纪律（MANDATORY · A+B 档 · 2026-07-16 落地 · 2026-07-22 扩展）**
- **Phase 完成定义**：某 Phase 标记为「完成」= 该 Phase 的高风险叙事模块已跑 `ddd-code-reviewer` + `ddd-phase-quality-gate` 问题清零（**0 open findings**），**且阶段完成时已跑 `codebase-optimizer`**（全库健康检查，含桩代码替换进度和生产就绪度）。仅文档/计划改动不计入。
- **质量结论标记**：每次提交 `src/` 改动前，须将质量门结果写入仓库根 `.quality-gate.json`（`cleared: true` + 三项 skill 结论 + 报告引用），并在 commit message 带一行 `Quality-Gate: <phase> cleared (0 open findings) [optimizer: <status>]`。
  - Phase 5 （过渡期）`codebaseOptimizer` 可写 `not_run`，钩子仅校验字段存在。
  - Phase 6+ `codebaseOptimizer` 必须包含 `PASSED`。
- **自动拦截（已落地）**：`scripts/git-hooks/pre-commit` 在暂存含 `src/` 时强制校验 `.quality-gate.json` 已暂存且 `cleared: true` + `codebaseOptimizer` 字段存在；`commit-msg` 校验 message 含 `Quality-Gate:`。CI（`ci.yml` 的 `quality-gate` job）在 push/PR 含 `src/` 改动时同步校验。启用：`git config core.hooksPath scripts/git-hooks`（或跑 `scripts/install-hooks.ps1`）。
- **"至少跑三轮"说明**：三个 quality skill 是交互式的，钩子无法精确判定"跑了 3 轮"；实际可行的卡点是"**reviewer 报告存在且 0 open findings 才放行**"。建议把质量循环跑到 0 open（技能自身 retry 逻辑通常自然多轮），再写标记。
- 标记与模板约定见 `docs/quality/QUALITY-GATE.md`。

> 本项目已跑过一次设计评审：初评 **DESIGN NEEDS WORK**（4×P1 + 5×P2，无 P0 阻断），经附录 C 重写后复审升级为 **DESIGN READY**（P1 全部闭环，P2 进入排期），报告见 `docs/blueprint-architecture-review-2026-07-16.md`。
