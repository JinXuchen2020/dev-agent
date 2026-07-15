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
| `AgentPlatform.Api` | 表现层 — ASP.NET Core Web API 含 Agents/AgentRoles/ExecutionLogs 端点、Scalar、CORS |
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
- **多租户**: ITenantScoped + EF Core Global Query Filter（Phase 1 单租户）
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
| Phase 3 | 平台化 — 可视化编排、监控、自定义 AgentType | 📋 计划 |
| Phase 4 | 前沿特性 — Code Agent、压测、BDD 全量 | 📋 计划 |
