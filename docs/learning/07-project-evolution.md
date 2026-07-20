# 07. 项目演进：Phase 1 → 5 的设计思路

> 目标：理解为什么阶段是这个顺序，每个阶段解决什么问题，不做什么事。

---

## 7.1 阶段性概览

| Phase | 名称 | 定位 | 关键内容 |
|-------|------|------|----------|
| Phase 1 | 基础 MVP | 骨架 + 抽象（全 Stub） | 6 项目脚手架、DDD 分层、模型路由、SpecFlow BDD |
| Phase 2 | 多智能体工作流 | 真实业务逻辑 | 状态机引擎、Redis 缓存、AutoGen Agent、真实 PGVector、ExecutionLog |
| Phase 3 | 平台化 | 前端 + 监控 | React Web UI、Grafana 大盘、React Flow、OpenTelemetry、CI/CD |
| Phase 4 | 知识接地与加固（上线前必做） | 把声称完成实为存根的能力落地 | RAG 接真 PGVector、Critic fail-loud、DB 端分页、真 tokenizer 压缩 |
| Phase 5 | 前沿特性与收尾 | 优化 + 亮点 | Code Agent、Research Agent、性能压测、BDD 全量、简历作品集 |

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
│ 向量库             │ PgVectorStore Stub        │ 仍为 Stub（Phase 2/3 未落地）；真实 PGVector 排期 Phase 4 │
│ 工作流引擎         │ StubWorkflowEngine        │ Phase 2 才实现状态机           │
│ 代码沙箱           │ DockerCodeSandbox Stub    │ Phase 5 才需要真实沙箱         │
│ 工具执行器         │ NativeToolExecutor Stub   │ 返回常数字符串                │
│ Agent 编排        │ AutoGenAgentOrchestrator   │ Phase 2 才配置 AutoGen.NET    │
│ 用户认证           │ 跳过 JWT/Identity         │ Phase 2 按蓝图实现            │
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
| `PgVectorStore Stub` | PGVector 真实向量检索（排期 Phase 4，当前仍为 Stub） |
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

## 7.5 Phase 5 为什么是"前沿特性"

最后阶段补齐"有亮点但非核心"的功能：

| 特性 | 为什么放最后 |
|------|-------------|
| Code Agent（生成-运行-调试-修复闭环） | 依赖 Phase 2 的沙箱 + Phase 3 的前端 |
| Research Agent（多步调研） | 依赖 Phase 2 的 Agent 协作 |
| 性能压测 | 前面的 Phase 没改完架构，压测结果会变 |
| BDD 全量验收 | 前面的功能还没写，测试自然没通过 |
| 简历文档 | 项目做完再整理 |

---

## 7.6 设计原则变化

| 原则 | Phase 1 | Phase 2 | Phase 3 | Phase 4（加固） | Phase 5（前沿） |
|------|---------|---------|---------|-----------------|----------------|
| **测试策略** | Architecture + BDD | + Unit (状态机) | + Integration | + ddd-code-reviewer (RAG/Critic) | + Stryker |
| **性能目标** | 不关心 | 不关心 | 基准：P95 < 30s | 复盘：DB 端分页 | 优化：P95 < 10s |
| **安全** | 跳过 | 基本 | 完整 | Critic 闸保真 | 审计 |
| **文档** | 蓝图 + 阶段文档 | + 学习文档 | + API 文档 | RAG 落地说明 | + 简历作品集 |

---

## 7.7 如果你自己做项目，这个演进顺序值得借鉴

```
第一步：搭骨架（Phase 1）
  └─ 定义所有接口，全部 Stub，验证架构能跑通

第二步：填逻辑（Phase 2）
  └─ 替换 Stub 为真实实现，先做最核心的功能

第三步：加 UI + 监控（Phase 3）
  └─ API 稳定后再写前端，没有监控不上线

第五步：优化 + 亮点（Phase 5）
  └─ 性能、安全、文档、CV 亮点
```

---

## 参考代码

- `AGENT_PLATFORM_BLUEPRINT.md` §5 — 阶段任务清单
- `phases/phase-1-baseline-mvp.md`
- `phases/phase-2-multi-agent.md`
- `phases/phase-3-platformization.md`
- `phases/phase-5-advanced-features.md`
