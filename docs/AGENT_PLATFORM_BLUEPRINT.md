# 自研 Agent 编排平台 · 构建蓝图（C# 技术栈）

> **版本**：v1.5 | **最后更新**：2026-07-13 | **维护者**：架构组 | [完整变更日志](./CHANGELOG.md)

> 本文档面向 **vibe coding** 使用：以「自然语言规格 + 目录脚手架 + 分阶段任务清单」的形式组织，可直接喂给 AI 编码 Agent（如 Cursor / Claude Code / ZCode）按阶段生成代码。
>
> 核心立场：**核心平台用 C# 保证 DDD/BDD 工程规范，模型部署与前沿实验性功能用 Python 微服务保证灵活性，二者通过 HTTP/gRPC 解耦。**

> **修改日志**：
> - **v1.5** (2026-07-13)：移除 Swagger/Scalar 环境限制（所有环境默认启用）；launchUrl 从 `openapi/v1.json` 改为 `swagger`；anchored-summary/phase-docs/CHANGELOG 同步更新
> - **v1.4** (2026-07-10)：Phase 1 全部代码优化完成：UnitOfWorkBehavior 事件顺序修复、ConversationsController → MediatR、CostController 接口化、Db 凭据安全化、Scalar 环境限制、UpdatedAt 修复、空守卫补全、using 清理。蓝图同步更新：QuickStart URL/cURL 修正、Phase 1 清单勾选、目录树补充了 Conversations/ 和 SpecFlowTests、缺失的 Abstractions 补全、Workflow 项目标记 Phase 2 骨架、删除了 Aspirational Serilog 配置代以 ILogger 现状描述、补充 OpenAI:Key/环境变量文档。
> - **v1.3** (2026-07-09)：补充 DDD 铁律三条约束（DI 注册 / 实现层位置 / 接口定义位置）
> - **v1.2** (2026-07-09)：锁定 SK 版本为 1.30.0；明确 MediatR v12+ DI 指南；修正 QuickStart 启动命令；添加测试项目位置约定和 EF Core 聚合根映射说明
> - **v1.1** (2026-07-01)：新增 Section 八监控运维、附录 H 部署DevOps、附录 I API规范、附录 J 运行时日志管理；附录拆分为独立文件；C.8 角色可扩展性；G.8 前端架构详述；P0 性能目标
> - **v1.0** (基线)：完整蓝图初版——DDD 目录脚手架、阶段一~四任务清单、6 个附录

---

<a name="top"></a>

## 📋 目录

> 点击章节标题跳转。AI Agent 可直接按需搜索，各附录完全自包含。

| 章节 | 说明 |
| :--- | :--- |
| [**一、项目定位**](#一项目定位) | 平台能力覆盖 + 非功能目标 |
| [**二、技术栈选型对照表**](#二技术栈选型对照表) | Python vs C# 替换方案及匹配度 |
| [**三、DDD 分层架构（目录脚手架）**](#三ddd-分层架构目录脚手架) | 6 项目目录结构 + 关键代码示例 |
| [**四、BDD/TDD 工程化**](#四bddtdd-工程化) | Reqnroll (SpecFlow 继任者) Gherkin 验收 + 文件 SQLite 集成层 + **前端 E2E 以 playwright-bdd 驱动** + xUnit 单元测试 |
| [**五、分阶段任务清单**](#五分阶段任务清单可直接作为-vibe-coding-提示词) | 阶段一~四可执行 checklist |
| [**六、避坑清单**](#六避坑清单c-做-ai-的-4-个短板--对策) | C# 做 AI 的 4 个短板 + 对策 |
| [**七、关键设计原则**](#七关键设计原则喂给-ai-agent-的全局约束) | 6 条喂给 AI Agent 的全局约束 |
| [**八、监控与运维**](#八监控与运维) | 指标定义 / 埋点 / Dashboard / 告警 / 日志 / 性能目标 |
| [**九、安全与鉴权**](#九安全与鉴权) | JWT / RBAC / 多租户 / Prompt 注入 / 审计日志 |
| [**十、给 Vibe Coding 的使用说明**](#十给-vibe-coding-的使用说明) | 文档消费方式 / 快速开始 / 最佳实践 |
| [**十一、编码约定**](#十一编码约定) | 命名规范 / Git 工作流 / AI 约束 / 测试约定 / 文档维护 |
| [**十二、失败场景示例**](#十二失败场景示例) | 模型降级全链路日志 + 恢复操作 |
| [**附录 A：核心聚合字段与状态枚举**](./appendices/core-aggregates.md) | Agent / Workflow / 会话 聚合定义 |
| [**附录 B：状态机引擎迁移方案**](./appendices/state-machine-migration.md) | 自研 → CoreWF 三阶段迁移 |
| [**附录 C：多 Agent 协作机制详解**](./appendices/multi-agent-collaboration.md) | 6 种角色 / 管线协作 / 上下文 / 失败回退 / 可扩展性 |
| [**附录 D：多模型统一调用机制详解**](./appendices/model-routing.md) | Kernel / Router / 降级 / 重试 / 熔断 / 成本控制 |
| [**附录 E：vLLM 定位与推理引擎选型**](./appendices/vllm-deep-dive.md) | vLLM vs Ollama vs 商用 API 判断矩阵 |
| [**附录 F：能力扩展体系**](./appendices/capability-extension.md) | Tool / Skill / MCP 三层抽象 |
| [**附录 G：前端形态选型**](./appendices/frontend-architecture.md) | Web / Tauri / Electron / Photino 决策矩阵 |
| [**附录 H：部署与 DevOps**](./appendices/deployment-devops.md) | Docker Compose / CI/CD（阶段二实现）/ 环境管理 / 扩容方案 |
| [**附录 I：API 接口规范**](./appendices/api-spec.md) | 7 个资源域 REST API + SSE 流式定义 |
| [**附录 J：运行时日志管理**](./appendices/execution-log.md) | ExecutionLog 表 / SSE 推送 / 查询 API / 保留策略 |

---

<a name="一项目定位"></a>
## 一、项目定位

构建一个**企业级、强类型、可维护的自研 Agent 编排平台**，能力覆盖：

- 多模型路由（降级 / 重试 / 负载 / 成本控制）
- RAG（文档入库与召回）
- Tool Calling（函数调用）
- 多 Agent 协作（6 种预置角色 + 自定义 AgentType 扩展：需求 → 产品 → 架构 → 开发 → 测试 → 文档）
- 有状态工作流编排（分支 / 重试 / 回滚 / 持久化）
- 代码沙箱（生成-运行-调试-修复闭环）
- 平台化（多租户、权限、配置版本管理、监控）

### 1.1 非功能目标

| 目标 | 指标 | 说明 |
| :--- | :--- | :--- |
| 可用性 | 99.9%（每月 < 43min 不可用） | API 服务多实例部署 + 健康检查自动摘除故障节点 |
| 数据持久性 | 99.999% | PostgreSQL 主从同步 + 每日全量备份 + 7 天保留 |
| 最大并发租户 | ≥ 100 | 多租户共享 API 实例，TenantId 隔离 |
| 单租户最大用户数 | ≥ 50 | 每个租户独立 RBAC 角色分配 |
| 平均恢复时间 (MTTR) | < 30min | Docker Compose / K8s 自动恢复 + 健康检查 |
| 数据保留期 | 工作流定义：永久 / 执行日志：90 天 / 原始对话：30 天 | 可配置，过期自动清理（定时任务） |

---

<a name="二技术栈选型对照表"></a>
## 二、技术栈选型对照表

| 能力域 | Python 原方案 | C# 替代方案 | 匹配度 |
| :--- | :--- | :--- | :--- |
| 多模型统一调用 | LiteLLM | Semantic Kernel v1.30.0 + 自定义路由层 | 95% |
| 开源模型部署 | vLLM | **保留 Python** vLLM，C# 走 OpenAI 兼容接口调用 | 100% |
| 有状态工作流 | LangGraph | 自研状态机 + MediatR 领域事件 / CoreWF | 75% |
| 多 Agent 协作 | CrewAI | AutoGen.NET v0.4.0-dev-1 (prerelease) / SK Agent 模式 | 80% |
| 向量数据库 | Chroma | PGVector (PostgreSQL 扩展) | 100%（真实 embedding + 余弦检索，Phase 4 落地） |
| 后端接口 | FastAPI | ASP.NET Core Web API | 100% |
| 前端 | Streamlit | **React** (TypeScript + Vite) + Ant Design；桌面形态可选 **Tauri 2.0**（详见附录 G） | 100% |
| 代码沙箱 | 进程级真实执行 + Docker 桩(未接入) | ProcessCodeSandbox(唯一 ICodeSandbox 入口, 接入 ISandboxIsolation) / DockerCodeSandbox(Docker.DotNet 真实容器隔离, 经 DockerSandboxIsolation 复用) / OS 级隔离(JobObject 资源限额 + AppContainer 真实禁网, fail-safe 回退) / IsolationStrength 强度标注 | 双层 100%（Docker 强隔离默认 + F11 进程级兜底） |
| 缓存 / 短期记忆 | Redis | StackExchange.Redis 最新稳定版（兼容 .NET 9，目标 net10.0） | 100% |
| BDD 验收 | behave | **Reqnroll 3.x**（SpecFlow 继任者，Gherkin 语法 100% 兼容）+ xUnit；真 HTTP + 文件 SQLite 集成层 | 100% |
| CQRS / 领域事件 | — | **MediatR 12.4**（v12+ 内置 DI 注册 `AddMediatR`，无需独立包） | 100% |

---

<a name="三ddd-分层架构目录脚手架"></a>
## 三、DDD 分层架构（目录脚手架）

> **铁律**：领域层不依赖任何基础设施；依赖方向永远向内（外层依赖内层，内层不依赖外层）。
>
> **补充约束**：
> - **抽象接口定义在 Application.Abstractions**：所有供基础设施层实现的接口（`IModelClient`、`IVectorStore`、`IShortTermMemory` 等）必须放在 `Application/Abstractions/`，不可放在 Infrastructure 层定义。
> - **实现类必须放在 Infrastructure**：`SemanticKernelModelClient`、`PgVectorStore`、`NativeToolExecutor` 等实现类必须放在 Infrastructure 层，不可放在 Application 层。
> - **仓储接口定义在 Domain.Repositories，实现类注册在 Infrastructure.DependencyInjection**：`IAgentRepository` 等在 Domain 层定义接口，Infrastructure 层实现，需在 `DependencyInjection.cs` 中手动注册 `services.AddScoped<IAgentRepository, AgentRepository>()`。

```
src/
├── AgentPlatform.Domain/                 # 领域层（纯业务，零外部依赖）
│   ├── Aggregates/
│   │   ├── Agents/                        # Agent 聚合根
│   │   │   ├── Agent.cs                   # 聚合根 class，私有 setter
│   │   │   └── Events/AgentCreated.cs     # 领域事件
│   │   ├── Workflows/                     # 工作流聚合根
│   │   ├── Conversations/                 # 会话聚合根
│   │   └── ToolDefinitions/               # 工具定义聚合根
│   ├── ValueObjects/                      # 值对象（一律 record）
│   │   ├── TokenUsage.cs
│   │   ├── ModelEndpoint.cs
│   │   └── Money.cs
│   ├── Services/                          # 领域服务（跨聚合逻辑）
│   │   └── RoutingPolicyDomainService.cs
│   ├── Enums/                             # 枚举定义（7 个枚举文件）
│   └── Repositories/                      # 仓储接口（仅接口，实现在外层）
│       ├── IAgentRepository.cs
│       ├── IConversationRepository.cs
│       └── IWorkflowRepository.cs
│
├── AgentPlatform.Application/             # 应用层（用例编排）
│   ├── Agents/
│   │   ├── Commands/CreateAgent/
│   │   └── Queries/GetAgent/
│   ├── Conversations/                     # 会话 Command
│   │   └── Commands/
│   │       ├── CreateConversation/
│   │       └── SendMessage/
│   ├── Workflows/
│   │   └── Commands/RunWorkflow/
│   ├── Routing/
│   │   ├── Services/ModelRouter.cs        # 多模型路由策略
│   │   └── RoutingConstants.cs
│   ├── EventHandlers/                     # 领域事件处理
│   ├── Tools/
│   │   └── ToolCallingDispatcher.cs       # 统一工具调度（路由 Native/Skill/MCP）
│   └── Abstractions/                      # 16 个接口 + 3 个配置类
│       ├── IModelClient.cs                # 模型调用抽象（基础设施实现）
│       ├── IVectorStore.cs                # 向量库抽象
│       ├── ICodeSandbox.cs                # 代码沙箱抽象
│       ├── IModelRouter.cs               # 模型路由接口
│       ├── IResiliencePipelineProvider.cs  # Polly 重试管道抽象
│       ├── ICostController.cs            # 成本控制接口
│       ├── IUnitOfWork.cs                 # 工作单元接口
│       ├── ITenantProvider.cs             # 多租户提供者（Phase 1 默认值）
│       ├── IDomainEventBus.cs             # 领域事件总线
│       ├── IShortTermMemory.cs            # 短期记忆抽象
│       ├── IStepExecutor.cs               # 步骤执行抽象（含 StepContext/StepResult）
│       ├── IWorkflowEngine.cs             # 工作流引擎抽象
│       ├── IToolExecutor.cs               # 工具执行抽象
│       ├── IToolRegistry.cs               # 能力清单注册表
│       ├── ICommand.cs                    # MediatR ICommand 标记接口
│       ├── ModelDefaults.cs               # 默认模型配置类
│       ├── PricingSettings.cs             # 定价配置类
│       ├── RouterSettings.cs              # 路由配置类
│       └── TenantSettings.cs              # 租户配置类
│
├── AgentPlatform.Infrastructure/          # 基础设施层（实现领域接口）
│   ├── Models/
│   │   ├── SemanticKernelModelClient.cs   # SK 封装多模型调用
│   │   ├── StubModelClient.cs            # Stub 模型客户端
│   │   ├── ModelTelemetryDecorator.cs    # 模型调用遥测装饰器
│   │   └── RoutingMiddleware/             # 降级 / 重试 / 成本统计
│   ├── Persistence/
│   │   ├── AppDbContext.cs                # EF Core + PGVector
│   │   ├── Configurations/                # 实体映射
│   │   ├── TenantProvider.cs             # 租户提供者实现
│   │   └── Repositories/                  # 仓储实现
│   ├── VectorStore/
│   │   └── PgVectorStore.cs               # PGVector 实现 IVectorStore（Stub）
│   ├── Cache/
│   │   └── InMemoryShortTermMemory.cs     # ConcurrentDictionary 内存缓存（原名 RedisShortTermMemory）
│   ├── Sandbox/
│   │   ├── ProcessCodeSandbox.cs          # 进程级真实执行（唯一 ICodeSandbox 入口），接入 ISandboxIsolation
│   │   ├── DockerCodeSandbox.cs           # Docker.DotNet 真实容器隔离（Provider=Docker）；F34 起仅作为内部执行器被 DockerSandboxIsolation 复用
│   │   ├── ISandboxIsolation.cs           # OS 级隔离抽象（internal 策略接口）+ IsolationStrength Strength 属性
│   │   ├── DockerProbe.cs                 # Docker 守护进程一次性探测（IDockerProbe 单例, fail-safe 缓存 IsAvailable）
│   │   ├── DockerSandboxIsolation.cs      # 复用 DockerCodeSandbox 的容器强隔离（ISandboxIsolation, Strength=Strong）
│   │   ├── JobObjectSandboxIsolation.cs   # Windows Job Object 资源限额（作业/进程内存 + 活动进程数 + CPU 速率硬上限）
│   │   ├── AppContainerSandboxIsolation.cs# Windows AppContainer 真实禁网 + 内部叠加 JobObject（fail-safe 回退）
│   │   ├── NullSandboxIsolation.cs        # 非 Windows / Off 回退（仅环境标记缓解项）
│   │   ├── WindowsJobObject.cs            # Job Object P/Invoke 封装（IDisposable）
│   │   ├── WindowsAppContainer.cs         # AppContainer profile P/Invoke 封装（IDisposable）
│   │   └── ProcessCaptureHelper.cs        # 输出捕获 + 超时杀 + 退出码共享实现
│   ├── Tools/                             # 附录 F：三层能力执行器（NativeToolExecutor 已真实化 / Skill·MCP 为 Phase 6 占位）
│   │   ├── NativeToolExecutor.cs          # 原生工具真实 HTTP 执行（IToolExecutor 实现）
│   │   ├── SkillPackageExecutor.cs        # SK Plugin 调用（IToolExecutor 实现）
│   │   ├── McpClient.cs                   # MCP 协议调用（IToolExecutor 实现）
│   │   └── InMemoryToolRegistry.cs        # 内存工具注册表
│   ├── Agents/
│   │   └── AutoGenAgentOrchestrator.cs    # AutoGen 多 Agent（Stub，Phase 2 实现）
│   └── Workflows/
│       └── StubWorkflowEngine.cs          # 工作流引擎 Stub
│
├── AgentPlatform.Api/                     # 表现层（ASP.NET Core Web API）
│   ├── Controllers/
│   │   ├── AgentsController.cs
│   │   └── ConversationsController.cs
│   ├── Middleware/
│   │   └── CorrelationIdMiddleware.cs
│   ├── Models/
│   │   ├── AgentResponse.cs
│   │   ├── SendMessageRequest.cs
│   │   └── SendMessageResponse.cs
│   └── Program.cs                         # DI 注册、Scalar、ProblemDetails、CORS、HealthChecks
│
├── AgentPlatform.Workflow/                # Phase 2 骨架（目前仅空目录结构）
│   ├── Engines/                           # ← Phase 2 填充 CustomWorkflowEngine.cs
│   ├── StateMachine/                      # ← Phase 2 填充
│   ├── States/                            # ← Phase 2 填充
│   ├── Transitions/                       # ← Phase 2 填充
│   ├── Steps/                             # ← Phase 2 填充
│   ├── Persistence/                       # ← Phase 2 填充
│   └── Extensions/                        # ← Phase 2 填充
│
├── AgentPlatform.SpecFlowTests/           # BDD 验收测试 (Reqnroll + xUnit，真 HTTP + 文件 SQLite 集成层)
│   ├── Features/
│   │   └── AgentRouting.feature
│   └── Steps/
│       └── AgentRoutingSteps.cs
│
└── AgentPlatform.Web/                     # 前端（React + TypeScript + Vite，桌面形态可启用 Tauri，详见附录 G）
```

### DDD 落地的 4 个 C# 原生优势

| DDD 概念 | C# 实现 | 约束效果 |
| :--- | :--- | :--- |
| 值对象 | `public record TokenUsage(int Prompt, int Completion);` | 天生不可变 |
| 实体/聚合根 | `class` + 私有 setter / `init` | 强制通过领域方法改状态，杜绝贫血模型 |
| 领域事件 | MediatR `INotification` 发布订阅（通过 `IDomainEventBus` 适配器桥接，Domain 层不应直接依赖 MediatR） | 解耦限界上下文 |
| 仓储模式 | 领域层定义 `IAgentRepository`，基础设施实现 | 严格依赖倒置 |

### 3.1 Phase 2 依赖版本锁定

| 依赖包 | 版本 | 说明 |
| :--- | :--- | :--- |
| AutoGen.NET | v0.4.0-dev-1 (prerelease) | 多 Agent 协作核心框架，需添加 `--prerelease` 标志安装 |
| StackExchange.Redis | 最新稳定版 | 短期记忆与缓存，通过 `IConnectionMultiplexer` 集成 |
| Semantic Kernel | v1.30.0 | 模型调用抽象层，已验证与 .NET 9 兼容 |

> **API 签名验证**：Phase 2 启动前需确认以下 API 签名与目标版本一致：
> - `AutoGen.Core.Agent` 构造器签名
> - `StackExchange.Redis.ConnectionMultiplexer.ConnectAsync`
> - `Microsoft.SemanticKernel.Kernel.CreateBuilder`

---

<a name="四bddtdd-工程化"></a>
## 四、BDD / TDD 工程化

### Reqnroll BDD（Gherkin 直接生成测试，SpecFlow 继任者）

```gherkin
# Features/AgentRouting.feature
Feature: 多模型路由降级
  作为平台编排器
  我希望主模型失败时自动降级到备用模型
  以保证工作流不被中断

Scenario Outline: 主模型超时后降级到备用模型
  Given 主模型 "<Primary>" 调用超时
  When 路由层触发降级策略
  Then 应使用备用模型 "<Fallback>" 重试

  Examples:
  | Primary   | Fallback  |
  | gpt-4o    | deepseek  |
  | deepseek    | gpt-4o      |
```

- Reqnroll 自动绑定 `[Binding]` 步骤（与 SpecFlow 语法 100% 兼容），与业务实现一一对应
- 接入 CI/CD：每次构建自动跑全量 BDD 验收用例（F27 已将 BDD 重定义为「最终集成测试层」= 真 HTTP + 文件 SQLite，经 `scripts/integration.mjs` + `ci.yml` integration job 作为合并前闸门）

### 前端 E2E（playwright-bdd，Gherkin 驱动）

> F27 收尾（2026-08-04）：前端 E2E 由裸 `@playwright/test` 的 `.spec.ts` 升级为 **playwright-bdd** 驱动的 Gherkin BDD，与后端 Reqnroll 同属「BDD 集成层」。

- 工具：**playwright-bdd**（`createBdd(test)`，`test` 须 `extend` 自 `playwright-bdd` 自带 `test`）+ `@playwright/test` 运行器 + 本机 Edge（`channel:'msedge'`）。
- 目录：`src/AgentPlatform.Web/e2e/features/*.feature`（Gherkin 场景）+ `e2e/steps/*.steps.ts`（步骤）+ `e2e/steps/fixtures.ts`（自定义 fixture）。
- 运行链路：先 `bddgen`（生成测试到 `e2e/.features-gen`，已被 .gitignore 忽略）→ 再 `playwright test`（脚本 `npm run e2e` = `bddgen && playwright test`）。
- 与后端 BDD 共用同一套 `Integration` 后端夹具（集成租户 + ApiKey + 示例工作流），经顶层闸门 `node scripts/integration.mjs --e2e` 一并跑（真 HTTP + 文件 SQLite + 真实浏览器）。
- **约定（feature-builder 硬约束 #7）**：任何触及 UI 的 feature 必须配套 BDD 前端 E2E，至少覆盖一条核心用户路径；禁止再写裸 `.spec.ts` 作 feature E2E（既有 `smoke.*.spec.ts` 属冒烟基线，除外）。

### TDD 栈

- 单元测试：**xUnit**
- Mock：**NSubstitute**（领域层纯单元测试，无 IO 依赖）
- 集成测试：**WebApplicationFactory**（接口级，无需额外工具）
- BDD 集成层：**Reqnroll** + `IntegrationAppFactory : WebApplicationFactory<Program>`（环境 `Integration` + 文件 SQLite `test-integration.db`），全量经真 HTTP 走完整管线（认证/限流/异常处理器/MediatR+UoW/EF），零 mock Repository、零 in-memory（Api.Tests 的 in-memory SQLite 仅作轻量 HTTP 契约测，不计入 BDD 层）

---

<a name="五分阶段任务清单"></a>
## 五、分阶段任务清单（可直接作为 vibe coding 提示词）

> 每个阶段的详细学习目标、验收标准、进度追踪见 [`phases/`](./phases/) 目录下的独立文档。
> - [阶段一：基础 MVP](./phases/phase-1-baseline-mvp.md) · [阶段二：多智能体工作流](./phases/phase-2-multi-agent.md)
> - [阶段三：平台化与模型优化](./phases/phase-3-platformization.md) · [阶段四：知识接地与加固](./phases/phase-4-grounding.md) · [阶段五：安全加固（launch-blocking）](./phases/phase-5-security-hardening.md) · [阶段六：前沿特性与收尾](./phases/phase-6-frontier-features.md)

### 阶段一 · 基础 MVP（1–2 周）

> 提示词示例：「按 `AGENT_PLATFORM_BLUEPRINT.md` 第三章脚手架创建解决方案与 6 个项目，完成阶段一所有任务，严格遵守 DDD 分层依赖方向。」

- [x] 初始化 .NET 9 解决方案，按第三章创建 6 个项目（Domain / Application / Infrastructure / Api / Workflow / Web）
- [x] 配置项目引用方向（Api → Application → Domain；Infrastructure → Application；Workflow → Application）
- [x] **模型路由**：用 Semantic Kernel 封装 `IModelClient`，实现 `SemanticKernelModelClient`
- [x] 自定义路由中间件：降级、重试（Polly）、负载、成本统计（token + 费用报表）
- [x] **vLLM**：独立部署为服务，以 OpenAI 兼容接口接入 SK
- [x] **RAG 接地**：`PgVectorStore` 已接真实 PGVector——`Ingest/Search/Delete` 走真实 embedding（`ITextEmbeddingGenerationService`）+ 余弦相似度检索，`IVectorStore` 召回物注入 `WorkflowContext.Retrieval`（验收标准见 `phase-4-grounding.md`）。
- [x] **Tool Calling**：SK Plugin 原生实现，定义 `ToolDefinition` 聚合
- [x] 写第一个 SpecFlow 验收场景：模型降级

**阶段一验收**：能通过 API 发起一次带工具调用的 RAG 对话，并生成成本报表。

### 阶段二 · 多智能体工作流（2–3 周）

- [ ] 用 **AutoGen.NET** v0.4.0-dev-1 定义 5 种 Agent 角色与协作规则（替代 CrewAI）；通过 `AutoGenSettings` 配置各 Agent 超时与模型分配
- [ ] **IOptions 配置绑定**：定义 `AutoGenSettings`、`RedisSettings`、`StateMachineSettings`、`ExecutionLogSettings` 为 `IOptions<T>` 绑定配置类，在 `appsettings.json` 提供默认值，通过 DI 容器注入到对应服务
- [ ] **AgentRole 枚举 → AgentType 值对象改造**：将 `AgentRole` 从 enum 改为 `record AgentType(Code, DisplayName, Description)`，保持 6 个预置角色向后兼容（详见附录 C.8）
- [ ] **更新仓储层**：`IAgentRepository.GetByRoleAsync` 参数改为 `string roleCode`，数据库列映射从 int 改为 Code 字符串
- [ ] **Agent 配置界面**：实现用户自定义角色创建表单（Code / 名称 / 描述 / 图标）
- [ ] 实现自研状态机（`AgentPlatform.Workflow`）：分支 / 重试 / 回滚；通过 `StateMachineSettings` 配置步骤超时与重试策略
- [ ] MediatR 领域事件贯穿工作流生命周期
- [ ] 状态持久化：短期记忆 StackExchange.Redis（通过 `RedisSettings` 配置连接字符串与缓存过期时间），长期记忆 EF Core + PostgreSQL
- [ ] 跑通完整流水线：**需求 → 产品 → 架构 → 开发 → 测试 → 文档**
- [ ] **ExecutionLog 写入**：状态机步骤执行后写入 `execution_logs` 表，通过 MediatR 领域事件触发；日志保留策略由 `ExecutionLogSettings` 管理

**阶段二验收**：输入一个需求，5 个 Agent 协作产出架构设计 + 代码 + 测试 + 文档。

### 阶段三 · 平台化与模型优化（2–3 周）

- [ ] **后端服务**：ASP.NET Core Web API，启用 Swagger 接口文档
- [ ] **Agent 配置模块**：YamlDotNet 解析配置 + EF Core 持久化 + 版本管理
- [ ] **工作流编排模块**：基于自研状态机，**React Flow** 拖拽可视化配置
- [ ] **前端**：**React** (TypeScript + Vite + Ant Design)，通过 REST API 对接后端
- [ ] **监控**：OpenTelemetry 接入 Prometheus + Grafana（原生支持），按 8.1 定义的全部指标完成埋点
- [ ] **自定义 AgentType 后端**：AgentType 种子数据 + 租户级自定义 CRUD API + Code 字符串映射
- [ ] **前端角色面板**：预置 + 自定义角色分区展示，角色选择从硬编码改为 API 动态加载
- [ ] **性能基准验证**：验证首版性能指标（单租户并发 5 工作流、步骤 P95 延迟 < 30s -> 迭代优化至 10s）
- [ ] **ExecutionLog 查询 API**：实现 4 个端点（列表 / 步骤 / 详情 / 错误筛选）
- [ ] **SSE 进度推送**：状态机执行时推送 `step_progress` 事件到前端
- [ ] **前端进度面板**：步骤列表中实时展示当前步骤状态和进度条
- [ ] **日志清理 Job**：定时删除 90 天前的日志、清理 30 天前的 payload

- [x] **发布工作流为 API / MCP Server**：`POST /api/v1/published-workflows/{slug}`（API Key 鉴权）+ `POST /api/v1/mcp`（平台内 JSON-RPC 2.0 `tools/list`/`tools/call`，无独立进程/端口），多租户隔离 + 审计（F22 实现，质量报告 `docs/quality/f22-publish-api-mcp-gate.md`）
- [x] **模板市场 / 示例库**：8 条平台级种子模板（覆盖全 8 分类）+ `GET /api/v1/workflow-templates[.../categories]/{id}` 浏览预览 + `POST /api/v1/workflow-templates/{id}/clone`（Admin/Operator 克隆为当前租户工作流，Agent 解绑 + 审计），多租户隔离（F23 实现，质量报告 `docs/quality/f23-template-market-gate.md`）
- [x] **执行 Trace / 评估视图**：`ExecutionLogEntry` 增 TokensIn/TokensOut/NodeType（Trace 三列）+ `ExecutionLogDetailPage` 节点级可观测；`EvaluationDataset`（ITenantScoped）聚合 + `POST/PUT/DELETE/GET /api/v1/evaluation-datasets` + `POST /{id}/run`（克隆工作流逐 case 跑编排、Exact/Contains 比对、汇总通过率/逐 case 报告），多租户隔离 + 审计（F24 实现，质量报告 `docs/quality/f24-execution-trace-gate.md`）

**阶段三验收**：平台具备配置管理、可视化编排、监控大盘、<strong>自定义 Agent 角色</strong>。

### 阶段四 · 前沿特性与收尾

- [ ] **Code Agent 闭环**：Docker.DotNet 操作沙箱，生成-运行-调试-修复闭环
- [x] **Research Agent**：`POST /api/v1/research` 联网多步调研（plan→search×N→synthesize），真实 SerpAPI HTTP + SSE 进度流（F6 实现）
- [ ] **性能压测与优化**：达到 P0 目标（单租户并发 5 工作流、步骤 P95 延迟 < 10s、模型调用 P95 < 15s）
- [ ] 补全 BDD 全量验收用例，接入 CI/CD（阶段二实现）
- [ ] 整理文档与简历作品集描述（.NET 工程化规范天然适合作为企业级作品）

---

<a name="六避坑清单c-做-ai-的-4-个短板--对策"></a>
## 六、避坑清单（C# 做 AI 的 4 个短板 + 对策）

| 短板 | 影响 | 对策 |
| :--- | :--- | :--- |
| 前沿 Agent 工具滞后（1–3 个月） | 最新范式 C# 生态延迟 | 核心平台用 C#；前沿实验功能用 Python 微服务，API 解耦 |
| 本地推理性能弱于 vLLM | 进程内跑大模型慢 | **永远不要在 C# 进程内跑大模型**；vLLM 独立部署，C# 只调用 |
| 小众第三方 SDK 缺失 | 部分搜索 API / 小众向量库无官方 SDK | 用 `HttpClient` 自行封装，工作量可忽略 |
| 学习曲线（仅限纯 Python 背景） | 需熟悉 DI / EF Core / MediatR | 有 .NET 基础则不存在；否则优先补 DI 和 EF Core |

---

<a name="七关键设计原则喂给-ai-agent-的全局约束"></a>
## 七、关键设计原则（喂给 AI Agent 的全局约束）

1. **强类型优先**：值对象一律 `record`，聚合根私有 setter，禁用动态类型绕过编译期检查。
2. **依赖倒置**：领域层只定义接口（`IModelClient` / `IVectorStore` / `ICodeSandbox`），实现在基础设施层，DI 注册在 Api 层。
3. **领域逻辑不外泄**：业务规则只能写在 `Domain` 项目；Application 只做用例编排，不做业务判断。
4. **状态变更经领域事件**：所有状态变更发布 MediatR 领域事件，副作用在 EventHandler 中处理。
5. **模型调用永远走 HTTP**：不在 C# 进程内做模型推理，vLLM / 商用模型统一走 OpenAI 兼容接口。
6. **BDD 驱动验收**：每个阶段交付前，SpecFlow 验收用例必须全部通过。

---

<a name="八监控与运维"></a>
## 八、监控与运维

> **铁律**：一个没有可观测性的企业级平台等于盲飞。所有模型调用、Agent 协作、工作流步骤、资源使用必须在第一次上线前就具备完整监控。

### 8.1 核心指标定义

| 指标类别 | 指标名称 | 采集方式 | 告警阈值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| **模型调用** | `model_call_total` | OpenTelemetry Counter | — | 按模型/provider 维度统计调用量 |
| | `model_call_duration_ms` | OpenTelemetry Histogram | P95 > 15s → Warning | 模型响应延迟，区分流式与非流式 |
| | `model_call_error_rate` | 计数器 / 总数 | > 5% → Critical | 降级/重试之外仍然失败的比例 |
| | `model_token_usage` | 每次调用记录 token | — | 按模型统计 token 消耗，用于成本核算 |
| **工作流** | `workflow_step_duration_ms` | Histogram | P95 > 30s → Warning | 单步执行耗时（含 Agent 调用） |
| | `workflow_success_rate` | Counter <br/>(需 `rate()` 计算比率) | < 90% → Critical | 工作流整体完成率 |
| | `agent_queue_depth` | Gauge | > 50 → Warning | Agent 等待队列深度 |
| **系统** | `request_throughput` | Counter | — | API 请求吞吐量 (RPS) |
| | `active_connections` | Gauge | — | 当前活跃 WebSocket 连接数 |
| | `memory_usage_mb` | Gauge | > 80% → Warning | 进程内存使用 |
| | `docker_sandbox_count` | Gauge | > 10 per host → Warning | 代码沙箱容器数量 |

### 8.2 埋点策略（Phase 3 范围 — 当前仅 ILogger 埋点）

```csharp
// Infrastructure/Observability/ModelTelemetryDecorator.cs
public class ModelTelemetryDecorator : IModelClient
{
    private readonly IModelClient _inner;
    private readonly ILogger<ModelTelemetryDecorator> _logger;

    public async Task<ModelResponse> ChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.ChatAsync(modelId, messages, ct);
            sw.Stop();
            _logger.LogInformation(
                "Model {ModelId} call succeeded in {Elapsed}ms, tokens: {Tokens}",
                modelId, sw.ElapsedMilliseconds, result.TokenUsage?.TotalTokens);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Model {ModelId} call failed after {Elapsed}ms",
                modelId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

> **设计原则**：埋点使用 Decorator 模式包裹原始调用（`IModelClient`），不侵入业务逻辑。注册时用 DI 组合即可。
> **当前状态**：Phase 1 仅使用 `ILogger` 埋点。`AppMetrics`（Counter/Histogram）等内容待 Phase 3 OpenTelemetry 集成时实现，参见 `phases/phase-3-platformization.md`。

### 8.3 Dashboard 设计（Grafana）

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Agent Platform · 监控大盘                                              │
├───────────────────────────┬───────────────────────────┬─────────────────┤
│  模型调用总览              │  工作流状态               │  系统健康        │
│                           │                           │                 │
│  请求量  │  P95延迟  │ 错误率│  运行中 │ 成功率 │ 排队│ 内存 │ CPU │ 连接│
│  ┌─────┐ │ ┌─────┐ │ ┌──┐  │  ┌──┐   │ ┌──┐  │ ┌─┐ │ ┌──┐ │ ┌─┐ │ ┌─┐ │
│  │ 1.2K│ │ │ 8.7s│ │ │2%│  │  │ 5│   │ │94%│  │ │3│ │ │68%│ │35%│ │42│ │
│  └─────┘ │ └─────┘ │ └──┘  │  └──┘   │ └──┘  │ └─┘ │ └──┘ │ └─┘ │ └─┘ │
├──────────┴──────────┴───────┴──────────┴───────┴─────┴──────┴─────┴─────┤
│  模型延迟趋势（过去 24h）                                                │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │   ~~~~~╱╲~~~~╱╲~~~~~~~~╱╲~~~~~╱╲~~~~~╱╲~~~~~~~~~~                │    │
│  │  ~~~╱  ╲╱  ╲╱  ╲~~~~~╱  ╲╱  ╲╱  ╲  ╱╲~~~~~╱╲~~~~                │    │
│  │  ─── gpt-4o (P50) ─── deepseek (P50) ─── claude (P50)            │    │
│  └─────────────────────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────────────────────┤
│  Agent 工作流耗时分解（Top 10 最慢步骤）                                 │
│                                                                         │
│  Step: 开发工程师 (生成代码)  ━━━━━━━━━━━━━━━━━━━━━━━━ 42.3s            │
│  Step: 测试工程师 (执行测试)  ━━━━━━━━━━━━━━━━━━━━      35.1s            │
│  Step: 架构师 (设计方案)      ━━━━━━━━━━━━━              12.5s            │
└─────────────────────────────────────────────────────────────────────────┘
```

### 8.4 告警规则

| 规则名称 | 条件 | 严重级别 | 通知方式 | 处理建议 |
| :--- | :--- | :--- | :--- | :--- |
| **模型调用故障** | `model_call_error_rate > 5%` 持续 5min | Critical | IM 群 + Email | 检查模型 Provider 状态，触发降级策略 |
| **步骤延迟过高** | `workflow_step_duration_ms P95 > 30s` 持续 3min | Warning | IM 群 | 检查 Agent 是否被限流，模型是否过载 |
| **队列积压** | `agent_queue_depth > 50` | Warning | IM 群 | 考虑扩容 Agent 实例或限制新工作流创建 |
| **沙箱资源耗尽** | `docker_sandbox_count > 10` 持续 1min | Warning | IM 群 | 清理僵尸沙箱，检查是否有内存泄漏 |
| **工作流成功率低** | `workflow_success_rate < 90%` 持续 10min | Critical | IM 群 + 电话 | 排查工作流引擎状态，检查步骤执行日志 |

### 8.5 日志采集

```csharp
// Phase 1 结构化日志：ILogger + CorrelationIdMiddleware
// Phase 2 迁移至 Serilog + Seq（详见 phases/phase-3-platformization.md）

// appsettings.json（Phase 3 追加 Serilog.WriteTo）
// {
//   "Serilog": {
//     "MinimumLevel": { "Default": "Information" },
//     "WriteTo": [
//       { "Name": "Console" },
//       {
//         "Name": "Seq",
//         "Args": { "serverUrl": "http://seq:5341" }
//       }
//     ],
//     "Enrich": [ "WithTenantId", "WithCorrelationId" ]
//   }
// }

// 统一日志上下文
public class CorrelationIdMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
```

### 8.6 P0 性能目标

| 指标 | 目标值 | 测量方式 | 对应告警 |
| :--- | :--- | :--- | :--- |
| 单租户并发工作流数 | ≥ 5 | 压测脚本创建 5 个工作流同时运行 | 队列积压 > 50 |
| 工作流步骤 P95 延迟 | < 10s | 从步骤开始执行到完成（含模型调用 + Agent 处理） | 步骤延迟 > 30s Warning |
| 单步模型调用 P95 延迟 | < 15s | 模型响应时间（含重试和降级） | 模型调用 > 15s Warning |
| API 接口 P99 延迟 | < 500ms | Web API 请求响应时间（不含模型调用） | — |
| 工作流成功率 | > 95% | (成功完成数 / 总执行数) | < 90% Critical |
| 部署回滚时间 | < 10min | 从发现故障到回滚完成 | — |
| 代码沙箱冷启动 | < 5s | Docker 容器从创建到就绪 | — |

> 验证节奏：阶段三末跑通基准（步骤 P95 < 30s），阶段四末优化至 P0 目标。每次合并主干前 CI 自动跑回归压测。

> **一句话总结**：通过 OpenTelemetry 采集模型调用、工作流执行、系统资源三类指标，用 Decorator 模式无侵入埋点，Grafana 大盘实时展示，5 条核心告警规则覆盖故障场景，Serilog 结构化日志关联 CorrelationId 支撑根因分析。

---

<a name="九安全与鉴权"></a>
## 九、安全与鉴权

> **实现阶段**：阶段五（安全加固）落地认证 / 多租户 / RBAC / 审计；F2 进一步将 JWT 改为 Cookie 承载并接入 PBKDF2 真实密码校验。QuickStart 模式不强制鉴权（FallbackPolicy 放行），但登录与密码校验逻辑已实现。
>
> **铁律**：平台会执行代码沙箱、调用外部模型，安全是第一优先级，不是"以后再补"。

### 9.1 认证与授权

| 层面 | 方案 | 说明 |
| :--- | :--- | :--- |
| 用户认证 | **自定义 User 聚合 + Cookie 承载 JWT + PBKDF2 密码哈希** | 登录颁发 JWT 写入 `ap_access_token` cookie（HttpOnly + SameSite=Lax + Secure=IsHttps + MaxAge=1h）；密码以 PBKDF2-SHA256（10 万迭代 + 16B 盐）哈希存储，固定时间比对。**无 ASP.NET Core Identity、无 Refresh Token**（cookie 过期即重新登录） |
| 多租户隔离 | **TenantId 字段 + EF Global Query Filter** | 每个 SQL 查询自动追加 `WHERE TenantId = @CurrentTenant`，杜绝跨租户数据泄露。所有实体已实现 `ITenantScoped` 接口，`AppDbContext.OnModelCreating` 已通过 `ITenantProvider` 配置 `HasQueryFilter`。当前使用配置中的 `DefaultTenantId`（单租户模式），**阶段五安全加固改为 per-request 动态求值** |
| API 权限 | **基于角色的访问控制（RBAC）** | Admin / Operator / Viewer 三级角色，Controller 用 `[Authorize(Roles = "Admin")]` 约束 |
| 服务间认证 | **内部 API Key / mTLS（可选）** | C# 平台调用 Python vLLM 服务时，Header 传递内部共享密钥 |

### 9.2 模型 API Key 管理

- **存储**：Key 使用 **AES-256-GCM** 加密后存入 PostgreSQL，密钥从环境变量 / Azure Key Vault 读取，明文永不落库
- **轮换**：支持 Key 版本化（`KeyVersion` 字段），过期时间 + 自动告警，零停机轮换
- **审计**：每次 Key 使用记录到 `AuditLog`（谁、何时、调用了哪个模型、消耗多少 token）

### 9.3 Prompt 注入防护

| 策略 | 实现 |
| :--- | :--- |
| 输入清洗 | 用户输入经过 **正则 + 长度限制 + 编码检测** 过滤，拒绝含嵌入式指令模式（如 `ignore previous instructions`）的内容 |
| 系统提示加固 | System Prompt 使用 **XML 标签隔离**（`<system>...</system>`），明确边界 |
| 输出过滤 | 模型返回结果经过 **敏感信息扫描**（正则检测邮箱/手机号/API Key 泄露），命中则脱敏或拦截 |
| 速率限制 | 每租户 + 每 API Key 的请求频率限制（**ASP.NET Core Rate Limiting**），防暴力探测 |

### 9.4 代码沙箱逃逸防护

- **网络隔离**：沙箱容器使用 `--network=none`（完全无网络）或自定义 `bridge` + 白名单域名
- **资源限制**：`--cpus=1 --memory=512m --pids-limit=64`，超限 Docker 自动 kill
- **执行超时**：默认 30 秒硬超时（`TimeoutToken` + `CancellationToken`），不可由生成的代码绕过
- **文件系统只读**：挂载宿主机目录时一律 `ro`，仅暴露 `/tmp` 可写
- **用户权限**：容器内以非 root 用户运行（`USER appuser`）

### 9.5 审计日志

所有关键操作写入结构化 `AuditLog` 表：

```
AuditLog
├── Id              (Guid, PK)
├── TenantId        (Guid, FK)
├── UserId          (Guid, FK)
├── ActionType      (enum: ModelCall / CodeExecute / ConfigChange / Login / KeyRotation)
├── ResourceType    (string: Agent / Workflow / ApiKey / Sandbox)
├── ResourceId      (Guid)
├── Detail          (JSON, 操作上下文)
├── IpAddress       (string)
├── CreatedAt       (DateTimeUtc)
```

- 审计日志**只追加、不可修改、不可删除**（应用层不暴露 Delete 接口，数据库层可考虑追加-only 表）
- **当前状态**：`AuditLog` 聚合 + `IAuditLogRepository` 已在 Phase 5 实现并接入业务 handler（Agent / Workflow / ApiKey / Knowledge 等）与 Key 三点位（KeyUsed / KeyRotation / KeyRevoked），审计写入已生效
- OpenTelemetry 中以 `log` 信号同步发出，便于 Grafana 实时告警

---

<a name="十给-vibe-coding-的使用说明"></a>
## 十、给 Vibe Coding 的使用说明

把本文件放进项目根目录后，推荐按以下顺序向 AI 编码 Agent 发起对话：

1. **「请阅读 `AGENT_PLATFORM_BLUEPRINT.md`，先初始化第三章的解决方案脚手架（6 个项目 + 引用方向），不要写业务逻辑。」**
2. **「按阶段一任务清单逐项实现，每完成一项跑测试，严格遵守第七章设计原则。」**
3. **「为阶段一写 SpecFlow BDD 验收场景，先红后绿。」**
4. 进入阶段二/三/四时，重复「阅读清单 → 实现 → BDD 验收」循环。

> 节奏建议：每个阶段结束后做一次 git commit，让 AI 先跑全量测试再提交。

> **当前 Stub 组件**（Phase 1 占位，Phase 2 替换为真实实现）：
> - `PgVectorStore`（总是返回模拟向量搜索结果）
> - `DockerCodeSandbox`（F9 已真实化：Docker.DotNet 真实容器隔离；**F34 起不再是并列 `ICodeSandbox`**，改为经 `DockerSandboxIsolation` 复用其内部容器执行能力，`Provider=Docker` 且守护进程可用时默认强隔离）
> - `NativeToolExecutor` / `SkillPackageExecutor` / `McpClient`（F10 已真实化：原生工具真实 HTTP、SK Plugin 真实调用、MCP SDK 真实连接/列举/调用；三者均经 `IToolExecutor` 分派）
> - `ProcessCodeSandbox` OS 级隔离（F11 已真实化：Windows JobObject 资源限额 + AppContainer 真实禁网，fail-safe 回退；`ISandboxIsolation` 抽象）+ **F34 双层**：`Provider=Docker` 且守护进程可用 → `DockerSandboxIsolation` 容器强隔离（复用 `DockerCodeSandbox`，结果标 `IsolationStrength.Strong`）；否则回退 F11 进程级（Weak）/非 Windows（None）；`SandboxResult.IsolationStrength` 回传强度供观测
> - `StubWorkflowEngine`（空实现）
> - `AutoGenAgentOrchestrator`（`Task.Delay(200)` + 返回字符串，未使用 AutoGen.NET）
> - `RoutingPolicyDomainService.EstimateCost`（总是返回 `Money.Zero`）
> - `StubModelClient`（条件注册，`ModelClient:Provider=Stub` 时启用）
> - `InMemoryToolRegistry`（内存实现，重启后不持久）
> - `AgentPlatform.Workflow` 项目（仅空目录骨架，零代码文件）

### 10.1 5 分钟快速开始（跳过 Docker）

> **场景**：你只想看一眼平台的长相，不想启动 PostgreSQL、Redis、vLLM 等外部依赖。
>
> 以下 `dotnet run` 命令自动使用 **10 个 stub 组件**（全部返回模拟响应或空实现），无需任何外部依赖：
>
> 1. **模型调用** — Stub 模拟回复
> 2. **数据库** — SQLite（代替 PostgreSQL）
> 3. **缓存** — MemoryCache（代替 Redis）
> 4. **向量库** — SQLite 内存模式（代替 PGVector）
> 5. **工作流引擎** — StubWorkflowEngine 空实现
> 6. **代码沙箱** — 禁用（代替 Docker）
> 7. **用户认证** — QuickStart 不强制鉴权（FallbackPolicy 放行），但登录 / 密码校验逻辑已实现
> 8. **Tool 执行器** — NativeToolExecutor 已真实化（真实 HTTP）/ SkillPackageExecutor 占位
> 9. **向量嵌入** — 空返回（不调用真实 Embedding API）
> 10. **通知/告警** — 空实现（不发送任何通知）

```bash
# 1. 克隆项目，进入源码目录
cd src/AgentPlatform.Api

# 2. 用 SQLite + Stub 模型配置运行（代替 PostgreSQL + 真实模型）
dotnet run --launch-profile QuickStart

# 3. 浏览器打开
open http://localhost:5000/scalar/v1

# 4. 创建会话（返回会话 ID）
CONV_ID=$(curl -s -X POST http://localhost:5000/api/v1/conversations \
  -H "Content-Type: application/json" | jq -r '.id')

# 5. 发送消息（返回模拟数据）
curl -X POST "http://localhost:5000/api/v1/conversations/$CONV_ID/messages" \
  -H "Content-Type: application/json" \
  -d '{"content":"Hello","model": "stub"}'
```

**QuickStart 配置要点**（`appsettings.QuickStart.json`）：

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Data Source=agent_platform_quickstart.db"  // SQLite
  },
  "ModelClient": {
    "Provider": "Stub",        // 不调用真实模型
    "StubResponse": "这是模拟回复，平台已正常运行。"
  },
  "Cache": {
    "Provider": "Memory",     // 内存缓存，不启动 Redis
    "Connection": null
  }
}
```

> 配置在 `src/AgentPlatform.Api/appsettings.QuickStart.json`（Git 已包含），`dotnet run --launch-profile QuickStart` 或设置 `ASPNETCORE_ENVIRONMENT=QuickStart` 自动加载。无需 Docker、无需真实 API Key、不消耗 token。

### 10.2 配置真实 API Key（非 QuickStart 模式）

运行真实模型（QuickStart 以外模式）需设置 OpenAI API Key：

```bash
# 方式一：.NET User Secrets（推荐，避免密钥落入 Git）
dotnet user-secrets set "OpenAI:Key" "sk-your-key-here"

# 方式二：环境变量
set ASPNETCORE_ENVIRONMENT=Development
set OpenAI__Key=sk-your-key-here

# 方式三：直接编辑 appsettings.Development.json
```

### 10.3 环境变量参考

| 环境变量 | 用途 | 默认值 |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | 运行时环境（Development/QuickStart/Production） | Production |
| `ConnectionStrings__PostgreSQL` | PostgreSQL 连接字符串（`Data Source=...` 开头为 SQLite） | 必填（Development/Production） |
| `OpenAI__Key` | OpenAI API Key | 空字符串（使用 Stub 时可忽略） |
| `ModelClient__Provider` | 模型客户端类型（`Stub` / `OpenAI`） | `OpenAI` |
| `Cache__Provider` | 缓存类型（`Memory` / `Redis`） | `Memory` |

---

<a name="十一编码约定"></a>
## 十一、编码约定

> **价值**：统一的编码约定让 AI 生成的代码风格一致，减少人工审查成本。以下约定直接喂给 AI Agent，从阶段一开始就遵循。

### 11.1 命名规范

| 类别 | 约定 | 示例 |
| :--- | :--- | :--- |
| 类 / 接口 / 记录 | PascalCase | `AgentType`, `IModelClient`, `TokenUsage` |
| 方法 / 属性 | PascalCase | `CreateAgent()`, `Role` |
| 私有字段 | `_camelCase` | `_agentRepository`, `_logger` |
| 参数 / 局部变量 | camelCase | `agentId`, `request` |
| 枚举 | PascalCase 单数 | `AgentStatus`, `WorkflowStatus` |
| 常量 | PascalCase | `DefaultTimeout`, `MaxRetryCount` |
| 文件夹 / 文件名 | PascalCase 匹配类型名 | `Agent.cs`, `WorkflowStore.ts` |

### 11.2 Git 工作流

- **分支策略**：`main`（只合入）+ `develop`（日常开发）+ `feature/*`（特性分支）
- **提交格式**：`<类型>(<范围>): <简要描述>`，如 `feat(model): add fallback routing`, `fix(workflow): correct retry count reset`
- **提交频率**：每个阶段结束（或每完成 3-5 个子任务）提交一次，确保 AI 有清晰的 checkpoint

### 11.3 AI 编码约束

向 AI Agent 下发任务时，**每次对话开头加上**以下提示：

```
约束：
1. 遵循附录索引 → 附录 X 中的代码定义，不要自己发明结构
2. 命名遵守 11.1 表，私有字段一律 _camelCase
3. 所有领域逻辑写在 Domain 项目，Application 只做编排
4. 值对象一律 record，聚合根私有 setter
5. 每个阶段结束前写 SpecFlow BDD 验收，先红后绿
6. 代码通过 dotnet build 无警告 + dotnet test 通过后才提交
```

### 11.4 测试约定

- 单元测试：xUnit + NSubstitute，测试类名 `{TargetClass}Tests`，方法名 `{Method}_{Scenario}_{Expected}`
- 集成测试：`WebApplicationFactory`，按资源域分组（`WorkflowIntegrationTests`）
- BDD 验收：Reqnroll（SpecFlow 继任者），`Features/` 目录下一 feature 一个 `.feature` 文件，`Steps/` 目录下一对一绑定；经 `IntegrationAppFactory`（文件 SQLite）跑真 HTTP 集成层
- **测试项目位置**：验收测试 / 集成测试放在 `src/` 目录（如 `src/AgentPlatform.AcceptanceTests`），单元测试放在对应项目下的 `Tests/` 目录或独立 `src/` 目录。与被测业务项目同属 `src/` 目录。

### 11.5 文档维护约定

| 场景 | 约定 |
| :--- | :--- |
| 修改附录内容 | 直接编辑 `appendices/` 下的对应 `.md` 文件，无需更新主文档的附录索引表 |
| 新增附录 | 在 `appendices/` 下新建 `.md` 文件，然后在主文档附录索引表中追加一行 |
| 修改主章节 | 直接编辑对应章节，同步更新 ToC 中的章节描述（如有必要） |
| 更新修改日志 | 每次签入文档变更前，在版本元数据块中添加一行 `- **vX.Y** (日期)：变更摘要` |
| 多人协作 | 通过 PR 修改本文件，变更至少 1 人 Review 后合入；附录内容可单人提交 |

---

## 十二、失败场景示例

> **价值**：只看成功路径编码，错误处理会薄弱。以下给出一个完整失败场景的日志、数据库状态和恢复操作，让 AI Agent 在编码时能覆盖对应的处理分支。

### 12.1 模型降级场景

**触发条件**：主模型 gpt-4o 连续 3 次调用超时（> 30s）

**日志输出**：

```
[2026-07-01 14:23:01] WARN  ModelRouter 模型 gpt-4o 调用超时 (32.1s)，第 1 次重试
[2026-07-01 14:23:35] WARN  ModelRouter 模型 gpt-4o 调用超时 (31.8s)，第 2 次重试
[2026-07-01 14:24:12] INFO  ModelRouter 模型 gpt-4o 已超过最大重试次数 (3)，
                    触发降级策略: deepseek (成本权重 0.3) → qwen (成本权重 0.5)
[2026-07-01 14:24:12] INFO  ModelRouter 已降级到 deepseek，本次调用成本 $0.0012
[2026-07-01 14:24:12] INFO  AuditLog    降级事件记录:
                    { "action": "model_fallback",
                      "primary": "gpt-4o", "fallback": "deepseek",
                      "reason": "timeout", "stepId": "step-3" }
```

**数据库状态查询**：

```sql
-- 查看降级后的工作流步骤状态
SELECT step_id, status, model_used, retry_count, duration_ms
FROM workflow_execution_steps
WHERE workflow_id = 'wf-abc-123'
ORDER BY step_order;

-- 结果：
-- step-1 | succeeded | gpt-4o  | 0 | 8234
-- step-2 | succeeded | gpt-4o  | 0 | 5211
-- step-3 | running   | deepseek| 3 | 96254   ← 降级后改用 deepseek
-- step-4 | pending   | null    | 0 | 0

-- 查看降级统计
SELECT model, fallback_count, total_cost
FROM model_routing_stats
WHERE tenant_id = 'tenant-123'
ORDER BY fallback_count DESC;
```

**恢复操作**：降级是自动的，无需人工干预。如果所有备用模型也失败，工作流进入 `Failed` 状态，人工恢复步骤：

```
1. 检查 model_routing_stats 表确认是哪个模型失败
2. 通过 POST /api/v1/workflows/{id}/retry 从失败步骤重试
3. 如果持续失败→配置新的备用模型→验证连通性→重新执行
```

---

---

## 附录索引

> 附录已拆分为独立文件，点击链接查看完整内容。
>
> **阅读路线**：
> - **初次通读**：附录 A（聚合定义）→ D（模型路由）→ F（能力扩展）
> - **按需查阅**：实现阶段任务时按附录索引直接跳转，各附录完全自包含
> - **阶段二补充**：标"阶段二补充"的附录尚未实现，内容仅作前瞻参考

| 附录 | 文件 | 内容概要 | 阶段 |
| :--- | :--- | :--- | :--- |
| **A** | [`appendices/core-aggregates.md`](./appendices/core-aggregates.md) | Agent / Workflow / Conversation 聚合根、值对象、状态枚举 | 阶段一 |
| **B** | [`appendices/state-machine-migration.md`](./appendices/state-machine-migration.md) | 自研状态机 → CoreWF 三阶段迁移方案 | 阶段二补充 |
| **C** | [`appendices/multi-agent-collaboration.md`](./appendices/multi-agent-collaboration.md) | 6 种 Agent 角色、管线协作、上下文传递、分支/并行、失败回退、角色可扩展性（C.8） | 阶段二补充 |
| **D** | [`appendices/model-routing.md`](./appendices/model-routing.md) | Semantic Kernel 集成、智能路由、降级/重试/熔断、成本控制 | 阶段一 |
| **E** | [`appendices/vllm-deep-dive.md`](./appendices/vllm-deep-dive.md) | vLLM vs Ollama vs 商用 API 选型矩阵 | 阶段一 |
| **F** | [`appendices/capability-extension.md`](./appendices/capability-extension.md) | Tool / Skill / MCP 三层能力抽象 | 阶段一 |
| **G** | [`appendices/frontend-architecture.md`](./appendices/frontend-architecture.md) | React 前端架构详述（zustand / React Query / Router / 权限 / React Flow）+ Tauri 双形态 | 阶段二补充 |
| **H** | [`appendices/deployment-devops.md`](./appendices/deployment-devops.md) | Docker Compose / 生产部署拓扑 / CI/CD（阶段二实现）/ 扩容策略 | 阶段二补充 |
| **I** | [`appendices/api-spec.md`](./appendices/api-spec.md) | 7 个资源域 REST API 规范 + SSE 流式协议 | 阶段二补充 |
| **J** | [`appendices/execution-log.md`](./appendices/execution-log.md) | ExecutionLog 表设计 / SSE 进度推送 / 查询 API / 保留与清理策略 | 阶段二补充 |
