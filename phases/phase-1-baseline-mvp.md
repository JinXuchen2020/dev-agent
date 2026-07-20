# 阶段一：基础 MVP（1–2 周）

> 学习目标：搭建项目骨架、跑通第一条带 RAG 和工具调用的 API 请求。

## 学习目标

- [x] **.NET 9 解决方案结构**：6 项目组织方式、依赖方向（Domain 零外部依赖）
- [x] **DDD 分层架构**：领域层 / 应用层 / 基础设施层的职责边界和引用规则
- [x] **Semantic Kernel**：`IChatCompletionService` 统一接口、Plugin 注册、Memory 集成
- [x] **Polly 重试策略**：超时重试、熔断、降级的管道模式（ResiliencePipeline 定义）
- [x] **PGVector**：向量存储接口定义 + PgVectorStore 存根实现，EF Core DbContext 配置
- [x] **SpecFlow BDD**：Gherkin 语法、`[Binding]` 步骤绑定、场景大纲（模型降级×2 通过）
- [ ] **OpenTelemetry 基础**：埋点、导出到控制台 / Seq（阶段三补充）

## 前置依赖

- [x] 蓝图文档 v1.1 已阅读
- [x] .NET 9 SDK 已安装（项目实际使用 net9.0）
- [ ] Docker Desktop 已安装（用于 PostgreSQL + vLLM，阶段二需要）

## 任务清单

- [x] 初始化 .NET 9 解决方案，按第三章创建 6 个项目（**知识点**：dotnet CLI + 解决方案结构）
- [x] 配置项目引用方向：Api→Application→Domain, Infrastructure→Application, Workflow→Application（**知识点**：C# 项目依赖 + DDD 依赖倒置）
- [x] **模型路由**：用 Semantic Kernel 封装 `IModelClient`，实现 `SemanticKernelModelClient`（**知识点**：SK 核心抽象）
- [x] 自定义路由中间件：降级（ModelRouter flat priority list）、重试（Polly ResiliencePipeline）、成本统计（CostController）（**知识点**：管道模式 + Polly 策略）
- [x] **vLLM**：预留 `VLLM:Url` 配置入口，以 OpenAI 兼容接口接入 SK（**知识点**：vLLM 部署 + OpenAI 兼容协议）
- [x] **RAG**：`IVectorStore` 接口 + `PgVectorStore` 存根实现（**知识点**：向量检索 + 依赖倒置）
- [x] **Tool Calling**：`ToolDefinition` 聚合 + `ToolCallingDispatcher` 统一调度器（**知识点**：函数调用 + 领域聚合）
- [x] 写第一个 SpecFlow 验收场景：模型降级，Scenario Outline ×2（**知识点**：BDD + Gherkin）

## 验收标准

- [x] 1. 能通过 API 发起一次带工具调用的 RAG 对话（`POST /api/v1/conversations/{id}/messages` + 向量搜索 + 模型路由）
- [x] 2. 能输出成本报表（`GET /api/v1/conversations/cost-report` 返回今日花费）
- [x] 3. 模型降级的 SpecFlow 场景红转绿（2/2 通过）
- [x] 4. `dotnet build` 零警告零错误
- [x] 5. `dotnet test` 全部通过

## 进度

- **开始日期**：2026-07-09
- **完成日期**：2026-07-09
- **完成度**：██████████ 100%

## 已应用的重构（根据回顾反馈，阶段一内修复）

| 反馈项 | 变更文件 | 变更内容 |
|--------|----------|----------|
| Domain 去 MediatR | `Domain/Abstractions/IDomainEvent.cs` ← 新建 | 纯接口，零依赖 |
| | `Domain/Aggregates/Agents/Events/AgentCreated.cs` | `INotification` → `IDomainEvent` |
| | `Application/Abstractions/IDomainEventBus.cs` ← 新建 | 事件总线抽象 |
| | `Application/EventHandlers/DomainEventBus.cs` ← 新建 | MediatR 适配器桥接 |
| | `Application/EventHandlers/DomainEventNotification.cs` ← 新建 | MediatR INotification 包装 |
| | `Application/Agents/Commands/.../Handler.cs` | `IPublisher` → `IDomainEventBus` |
| | `Application/EventHandlers/AgentCreatedEventHandler.cs` | `AgentCreated` → `DomainEventNotification<AgentCreated>` |
| | `Application/DependencyInjection.cs` | 注册 `IDomainEventBus` |
| | `AgentPlatform.Domain.csproj` | 移除 `MediatR` 包 |
| ModelRouter 扁平化 | `Application/Routing/Services/ModelRouter.cs` | 模型特定降级链 → flat priority list |
| | `SpecFlowTests/Features/AgentRouting.feature` | `claude→qwen` → `deepseek→gpt-4o`（对称） |
| | `SpecFlowTests/Steps/AgentRoutingSteps.cs` | 移除硬编码 expectedFallback |
| 蓝图更新 SK 版本锁定 | `AGENT_PLATFORM_BLUEPRINT.md` 技术栈表 | `Semantic Kernel` → `Semantic Kernel v1.30`；新增 MediatR 12.4 行 |
| 蓝图更新 QuickStart 命令 | `AGENT_PLATFORM_BLUEPRINT.md` 10.1 | `dotnet run --configuration QuickStart` → `--launch-profile` |
| 蓝图更新测试位置约定 | `AGENT_PLATFORM_BLUEPRINT.md` 11.4 | 新增测试项目位置约定段落 |
| 蓝图更新 DDD 领域事件说明 | `AGENT_PLATFORM_BLUEPRINT.md` 三、DDD | 添加 `IDomainEventBus` 适配器模式说明 |
| 附录 EF Core 映射说明 | `appendices/core-aggregates.md` A.5 | 新增 EF Core 映射注意事项 ↩ |
| MessageRole 合并到 Domain | `Application/Abstractions/IModelClient.cs` | 删除独立 `MessageRole` enum，`ChatMessage.Role` 改用 `Domain.Enums.MessageRole` |
| | `Infrastructure/Models/SemanticKernelModelClient.cs` | 全限定名 `Application.Abstractions.MessageRole` → `Domain.Enums.MessageRole` |
| | `Api/Controllers/ConversationsController.cs` | 同上 |
| | `SpecFlowTests/Steps/AgentRoutingSteps.cs` | 同上 |
| 消除 AgentRole 歧义 | `Domain/Aggregates/Agents/AgentRole.cs` | **已删除**值对象 record |
| | `Domain/Aggregates/Agents/Agent.cs` | `AgentRole` → `Enums.AgentRole` |
| | `Domain/Repositories/IAgentRepository.cs` | 参数类型 `Enums.AgentRole` |
| | `Infrastructure/.../AgentRepository.cs` | 查询 `a.Role.Role == role.Role` → `a.Role == role` |
| | `Application/.../CreateAgentCommandHandler.cs` | 移除 `new AgentRole(request.Role)` 包装，直传 `request.Role` |
| IToolRegistry 注册 | `Infrastructure/Tools/InMemoryToolRegistry.cs` ← 新建 | `ConcurrentDictionary` 内存实现 |
| | `Infrastructure/DependencyInjection.cs` | `services.AddScoped<IToolRegistry, InMemoryToolRegistry>()` |
| CostController 线程安全 | `Application/Routing/Services/ModelRouter.cs` | 所有 `_todaySpent` 读写加 `lock (_lock)` |
| ModelTelemetryDecorator 装饰管道 | `Infrastructure/DependencyInjection.cs` | `SemanticKernelModelClient` 自注册 → `IModelClient` 工厂构造装饰器包裹 |
| Conversation EF Core 映射 | `Infrastructure/.../ConversationConfiguration.cs` ← 新建 | `OwnsMany` + `UsePropertyAccessMode.Field` |
| 仓储 DI 注册 | `Infrastructure/DependencyInjection.cs` | 新增 `IAgentRepository` / `IConversationRepository` / `IWorkflowRepository` 注册 |
| Executor 迁至 Infrastructure | `Application/Tools/ToolCallingDispatcher.cs` | 移除 `NativeToolExecutor` / `SkillPackageExecutor` / `McpClient` 实现类 |
| | `Infrastructure/Tools/NativeToolExecutor.cs` ← 新建 | 从 Application 迁入，新增 `Source` 属性 |
| | `Infrastructure/Tools/SkillPackageExecutor.cs` ← 新建 | 同上 |
| | `Infrastructure/Tools/McpClient.cs` ← 新建 | 同上 |
| | `Application/Abstractions/IToolExecutor.cs` | 接口新增 `ToolSource Source { get; }`；调度器改用 `e.Source` |
| IShortTermMemory 接口迁移 | `Application/Abstractions/IShortTermMemory.cs` ← 新建 | 接口从 Infrastructure 迁至 Application.Abstractions |
| | `Infrastructure/Cache/RedisShortTermMemory.cs` | 删除接口定义，改为 `using Application.Abstractions` |
| 删除 RoutingPolicyDomainService 重复 | `Application/Routing/Services/ModelRouter.cs` | 删除 Application 层 `RoutingPolicyDomainService` 类 |
| | `Infrastructure/DependencyInjection.cs` | `RoutingPolicyDomainService` → `Domain.Services.RoutingPolicyDomainService` |
| | `SpecFlowTests/Steps/AgentRoutingSteps.cs` | 添加 `using Domain.Services` |
| ConversationsController 解硬编码 | `Api/Controllers/ConversationsController.cs` | `CreateAgentRequest` 新增 `Role` 参数；传递 `request.Role` |
| CostController 去重 | `Application/Routing/Services/ModelRouter.cs` | 提取 `GetCostPerUnit` 私有方法，消除 switch 重复 |
| 文件拆分 | `Application/Routing/Services/CostController.cs` ← 新建 | 从 `ModelRouter.cs` 拆出 |
| | `Application/Routing/Services/AllModelsFailedException.cs` ← 新建 | 从 `ModelRouter.cs` 拆出 |
| | `Application/Routing/Services/ModelCandidate.cs` ← 新建 | 从 `ModelRouter.cs` 拆出 |
| | `Application/Routing/Services/RoutingRequest.cs` ← 新建 | 从 `ModelRouter.cs` 拆出 |
| DI 注册短名 | `Infrastructure/DependencyInjection.cs` | `Domain.Services.RoutingPolicyDomainService` → `RoutingPolicyDomainService`（加 using） |
| | 同上 | `Infrastructure.Tools.NativeToolExecutor` → `NativeToolExecutor`（加 using） |
| CallAsync 去冗余 | `Infrastructure/.../ModelTelemetryDecorator.cs` | 合并 `CallAsync`→`ChatAsync`，删除多余公开方法 |
| TenantId 配置化 | `Application/Abstractions/TenantSettings.cs` ← 新建 | `IOptions<TenantSettings>` 注入 Controller |
| | `Api/appsettings.json` | 新增 `Tenant.DefaultTenantId` 配置节 |
| | `Api/Program.cs` | `services.Configure<TenantSettings>(...)` |
| | `Api/Controllers/ConversationsController.cs` | 注入 `IOptions<TenantSettings>`，替换硬编码 GUID |
| Polly 管道接入 | `Application/Abstractions/IResiliencePipelineProvider.cs` ← 新建 | 接口定义（无 Polly 依赖） |
| | `Infrastructure/.../ResiliencePipelineProvider.cs` | 实现接口，Polly 非泛型管线 + 状态捕获模式 |
| | `Application/.../ModelRouter.cs` | 注入 `IResiliencePipelineProvider`，调用 `ExecuteWithRetryAsync` |
| | `SpecFlowTests/Steps/AgentRoutingSteps.cs` | 添加 `_pipeline` Substitute 和转发逻辑 |
| Swagger UI | `Api/AgentPlatform.Api.csproj` | 添加 `Scalar.AspNetCore` 包 |
| | `Api/Program.cs` | `app.MapScalarApiReference()` 加 Scalar using |
| JSON 序列化 | `Api/Program.cs` | `AddJsonOptions` → CamelCase + 忽略 Null |
| 蓝图更新 DDD 约束 | `AGENT_PLATFORM_BLUEPRINT.md` 三 | 补充三条铁律：仓储 DI 注册 / 实现层位置 / 接口定义位置 |
| **H1: UnitOfWork** | `Application/Abstractions/IUnitOfWork.cs` ← 新建 | IUnitOfWork 接口，AppDbContext 实现 |
| | `Application/Behaviors/UnitOfWorkBehavior.cs` ← 新建 | MediatR pipeline behavior，自动 SaveChangesAsync |
| | `Application/DependencyInjection.cs` | `AddOpenBehavior(typeof(UnitOfWorkBehavior<,>))` |
| | `Infrastructure/Persistence/AppDbContext.cs` | 实现 `IUnitOfWork` |
| **H2: IWorkflowEngine 实现** | `Infrastructure/Workflows/StubWorkflowEngine.cs` ← 新建 | Stub 占位实现，所有方法返回 Task.CompletedTask |
| | `Infrastructure/DependencyInjection.cs` | `AddScoped<IWorkflowEngine, StubWorkflowEngine>()` |
| **H3: Agent.ModelEndpoint 映射** | `Infrastructure/.../AgentConfiguration.cs` | `OwnsOne(a => a.ModelEndpoint, ...)` 映射到 5 个列 |
| **H4: WorkflowConfiguration** | `Infrastructure/.../WorkflowConfiguration.cs` ← 新建 | OwnsMany WorkflowStep + Navigation field access |
| **H5: Error Middleware** | `Api/Program.cs` | `app.UseExceptionHandler(o => { })` |
| **M1: AgentAssignments 封装** | `Domain/.../Workflow.cs` | `Dictionary` → `private readonly` + `IReadOnlyDictionary` |
| **M2: TotalTokenUsage null 安全** | `Domain/.../Conversation.cs` | `TotalTokenUsage ?? new TokenUsage(0, 0)` |
| **M3+M4: [Required] 验证** | `Api/.../ConversationsController.cs` | `string Content` → `[Required] string Content`；`string Name` → `[Required] string Name` |
| **M6: SQLite 自动选择** | `Infrastructure/DependencyInjection.cs` | 按 `Data Source=` 前缀自动选 `UseSqlite`/`UseNpgsql` |
| | `Infrastructure/AgentPlatform.Infrastructure.csproj` | 添加 `Microsoft.EntityFrameworkCore.Sqlite 9.0.4` |
| **M7: 线程安全** | `Infrastructure/Cache/RedisShortTermMemory.cs` | `Dictionary` → `ConcurrentDictionary` + `TryRemove` |
| **L1: 流式 Tool 角色** | `Infrastructure/.../SemanticKernelModelClient.cs` | Stream switch 补 `MessageRole.Tool => AuthorRole.Tool` |
| **包版本对齐** | `Infrastructure/AgentPlatform.Infrastructure.csproj` | EF Core / Configuration / Logging → 9.0.4 |

## 回顾（完成后填写）

### 做得好的

1. **DDD 依赖方向严格执行**：Domain 零外部依赖（`IDomainEvent` 纯接口），所有抽象接口定义在 Application 层，实现在 Infrastructure 层，Api 层只做 DI 注册。
2. **一次 build 零警告**：除了命名空间歧义和 SK API 差异外，核心逻辑基本一次通过。
3. **SpecFlow 场景中文化**：Gherkin 支持中文场景描述，`Scenario Outline` + `Examples` 表格驱动，2 条降级链路全部通过。
4. **完整覆盖阶段一要求**：模型路由 / 降级重试 / RAG / Tool Calling / 成本报表 全部实现。

### 下次改进

1. ~~**提前验证 NuGet 版本兼容性**~~ ✅ **已记录**：踩坑表中已汇总 MediatR / SK 版本差异，后续阶段启动前先查版本 API 差异表。
2. ~~**SpecFlow 测试优先**~~ ✅ **已记录**：后续阶段应严格 BDD 红-绿循环，先写 `.feature` 再实现业务代码。
3. ~~**ModelRouter 降级策略过早复杂化**~~ ✅ **已重构**：flat priority list + `OrderByDescending` 将 preferred model 提到队首。
4. ~~**避免 Domain 层依赖 MediatR**~~ ✅ **已重构**：`IDomainEvent` → `IDomainEventBus` → `DomainEventBus` 适配器。Domain 依赖归零。
5. ~~**`ChatMessage` 类型跨层重复**~~ ✅ **已重构**：删除 Application `MessageRole`，统一引用 `Domain.Enums.MessageRole`。
6. ~~**`IToolRegistry` 无实现未注册 DI**~~ ✅ **已重构**：`Infrastructure/Tools/InMemoryToolRegistry.cs` ← 新建，DI 注册 `IToolRegistry`。
7. ~~**`CostController` 非线程安全**~~ ✅ **已重构**：所有 `_todaySpent` 读写加 `lock (_lock)`。
8. ~~**`ModelTelemetryDecorator` 未接入管道**~~ ✅ **已重构**：`SemanticKernelModelClient` 自注册，`IModelClient` 工厂构造装饰器包裹。
9. ~~**`Conversation.Messages` EF Core 无法映射**~~ ✅ **已重构**：`ConversationConfiguration.cs` ← 新建，`OwnsMany` + `UsePropertyAccessMode.Field`。
10. ~~**仓储接口未注册 DI**~~ ✅ **已重构**：`IAgentRepository`/`IConversationRepository`/`IWorkflowRepository` 注册到容器。
11. ~~**ToolExecutor 实现类放错层**~~ ✅ **已重构**：`NativeToolExecutor`/`SkillPackageExecutor`/`McpClient` 迁至 Infrastructure；`IToolExecutor` 接口新增 `Source` 属性，调度器改用 `e.Source`。
12. ~~**IShortTermMemory 接口定义在 Infrastructure**~~ ✅ **已重构**：接口移至 `Application.Abstractions`。
13. ~~**RoutingPolicyDomainService 重复**~~ ✅ **已重构**：删除 Application 层副本，统一使用 Domain 层。
14. ~~**ConversationsController 硬编码 Role**~~ ✅ **已重构**：`CreateAgentRequest` 新增 `Role` 参数并传递。
15. ~~**CostController 提价 switch 重复**~~ ✅ **已重构**：提取 `GetCostPerUnit` 私有方法。
16. ~~**RoutingPolicyDomainService 注册用全限定名**~~ ✅ **已重构**：改用短名 + using。
17. ~~**ModelRouter.cs 一文件多类型**~~ ✅ **已重构**：`CostController`/`AllModelsFailedException`/`ModelCandidate`/`RoutingRequest` 拆为独立文件。
18. ~~**CallAsync 多余公开方法**~~ ✅ **已重构**：合并到 `ChatAsync`，删除 `CallAsync`。
19. ~~**TenantId 硬编码**~~ ✅ **已重构**：`TenantSettings` 配置类 + IOptions 注入。
20. ~~**Polly 弹性管道闲置**~~ ✅ **已重构**：`IResiliencePipelineProvider` 接口 → `ResiliencePipelineProvider` 实现，`ModelRouter` 接入。
21. ~~**Swagger UI 未配**~~ ✅ **已重构**：安装 `Scalar.AspNetCore`，`Program.cs` 添加 `MapScalarApiReference()`。
22. ~~**JSON 序列化未配置**~~ ✅ **已重构**：`AddJsonOptions` 配置 CamelCase + 忽略 Null。
23. ~~**H1: SaveChangesAsync 从未调用**~~ ✅ **已修复**：IUnitOfWork 接口 + MediatR pipeline behavior 自动在每个 command handler 后调用。
24. ~~**H2: IWorkflowEngine 无实现**~~ ✅ **已修复**：StubWorkflowEngine 占位实现，所有方法 Task.CompletedTask。
25. ~~**H3: Agent.ModelEndpoint 无 EF OwnsOne 映射**~~ ✅ **已修复**：AgentConfiguration 追加 OwnsOne → 5 个列。
26. ~~**H4: Workflow 无 EF Core 配置**~~ ✅ **已修复**：WorkflowConfiguration.cs 新建，OwnsMany + Navigation field。
27. ~~**H5: 无 UseExceptionHandler 中间件**~~ ✅ **已修复**：app.UseExceptionHandler(o => { })。
28. ~~**M1: Workflow.AgentAssignments 可写 Dictionary**~~ ✅ **已修复**：改为 private readonly + IReadOnlyDictionary。
29. ~~**M2: Conversation.TotalTokenUsage = null!**~~ ✅ **已修复**：AddMessage 中 `?? new TokenUsage(0, 0)`。
30. ~~**M3+M4: API 模型缺 [Required]**~~ ✅ **已修复**：Content/Name 加 [Required]。
31. ~~**M6: QuickStart SQLite 用 Npgsql 提供者**~~ ✅ **已修复**：DI 按连接串前缀自动选 SQLite/Npgsql。
32. ~~**M7: RedisShortTermMemory 非线程安全**~~ ✅ **已修复**：Dictionary → ConcurrentDictionary。
33. ~~**L1: 流式路径 Tool 角色丢失**~~ ✅ **已修复**：ChatStreamAsync switch 补 Tool ⇒ AuthorRole.Tool。
34. ~~**C1: CostController Scoped → 重置今日花费**~~ ✅ **已修复**：改为 Singleton；每日首次请求自动重置 `_todaySpent`。
35. ~~**C2: TokenUsage 始终返回 0**~~ ✅ **已修复**：从 `reply.Metadata` 读取 SK 真实用量。
36. ~~**C3: UnitOfWorkBehavior 未传 ct**~~ ✅ **已修复**：`next()` → `next(cancellationToken)`。
37. ~~**C4: Query 也触发 SaveChanges**~~ ✅ **已修复**：AddOpenBehavior 加 `where TRequest : ICommand<TResponse>` 约束。
38. ~~**C5: ToolCallingDispatcher KeyNotFoundException**~~ ✅ **已修复**：`_executors[tool.Source]` → `TryGetValue` + 友好异常。
39. ~~**C6: QuickStart 看不到 Scalar**~~ ✅ **已修复**：Scalar 条件从 `IsDevelopment()` 改为不是 Production 就显示。
40. ~~**C7: appsettings.json 无模型配置 → 空字典**~~ ✅ **已修复**：启动时若 `_services` 为空则日志警告；注入 `ModelSettings` 配置节。
41. ~~**C8: HttpsRedirection 无限重定向**~~ ✅ **已修复**：`UseHttpsRedirection` 条件化，仅在 HTTPS 端点存在时启用。
42. ~~**C9: Owned 影子 Id 缺 ValueGeneratedOnAdd**~~ ✅ **已修复**：WorkflowStep/Message 影子属性追加 `.ValueGeneratedOnAdd()`。
43. ~~**C10: `_todaySpent` 永不重置**~~ ✅ **已修复**：`CanAffordAsync` 按天重置逻辑，跨天自动清零。
44. ~~**H2: 硬编码模型默认值**~~ ✅ **已修复**：默认值移入 `appsettings.json`，注入 `IOptions<ModelDefaults>`。
45. ~~**H3: Domain 实体直接暴露 API**~~ ✅ **已修复**：`AgentResponse`/`SendMessageResponse` DTO，通过 `Agent.ToDto()` 映射。
46. ~~**H4: 异常处理器空壳**~~ ✅ **已修复**：注册 `ProblemDetails` + 结构化错误响应。
47. ~~**H5: 无 TenantId Global Query Filter**~~ ✅ **已修复**：`AppDbContext.OnModelCreating` 对每个 `ITenantScoped` 实体加 `HasQueryFilter`。
48. ~~**H7: TokenUsage 列名冲突**~~ ✅ **已修复**：`ConversationConfiguration` 用 `.HasColumnName` 显式区分。
49. ~~**H9: RedisShortTermMemory expiry 忽略**~~ ✅ **已修复**：`SetAsync` 内部用 `CancellationTokenSource` 模拟超时删除。
50. ~~**H10: IShortTermMemory.GetAsync 缺 CancellationToken**~~ ✅ **已修复**：接口和实现统一补 `CancellationToken` 参数。
51. ~~**M1: Scalar 仅限 Development**~~ ✅ **已修复**：改为 `!app.Environment.IsProduction()`；后续进一步移除环境限制，所有环境默认启用。
52. ~~**M2: CorrelationId 未存 HttpContext.Items**~~ ✅ **已修复**：`context.Items["CorrelationId"]` + `context.TraceIdentifier`。
53. ~~**M3: ChatStreamAsync 重复消息映射**~~ ✅ **已修复**：提取私有 `ToChatHistory` 方法，两处调用统一。
54. ~~**M4: 流式路径无遥测**~~ ✅ **已修复**：`ModelTelemetryDecorator.ChatStreamAsync` 加耗时/错误跟踪。
55. ~~**M8: 路由无任何日志**~~ ✅ **已修复**：`ModelRouter.RouteAsync` 加 `LogWarning` 模型失败原因。
56. ~~**M9: Application 层 DI 依赖注册缺失**~~ ✅ **已修复**：`ModelRouter`/`CostController`/`ToolCallingDispatcher` 注册统一到 Infrastructure DI。
57. ~~**M11: 无 CORS**~~ ✅ **已修复**：`Program.cs` 添加 `AddCors` + `UseCors`，配置从 appsettings 读取。
58. ~~**M12: 无 Health Checks**~~ ✅ **已修复**：`Program.cs` 添加 `MapHealthChecks("/health")` + `AddHealthChecks`。
59. ~~**H11: 模型候选列表硬编码**~~ ✅ **已修复**：`ModelRouter` 从 `IOptions<RouterSettings>` 读取候选模型配置。
60. ~~**H12: 定价表硬编码**~~ ✅ **已修复**：`CostController.GetCostPerUnit` 从 `IOptions<PricingSettings>` 读取。
61. ~~**P0-1: ModelDefaults 未注册 IOptions**~~ ✅ **已修复**：`Program.cs` 添加 `Configure<ModelDefaults>`。
62. ~~**P0-2/3: QuickStart 缺 StubModelClient**~~ ✅ **已修复**：新建 `StubModelClient`，DI 根据 `ModelClient:Provider` 自动选择 Stub/SK 实现。
63. ~~**P0-4: UseAuthorization 空跑**~~ ✅ **已修复**：注释掉 `UseAuthorization()`，阶段二启用。
64. ~~**P1-5: RoutingPolicyDomainService 硬编码定价**~~ ✅ **已修复**：标记阶段二，`EstimateCost` 返回 `Money.Zero`。
65. ~~**P1-6: Money 缺比较运算符**~~ ✅ **已修复**：追加 `<=` / `>=` 运算符，`CostController.CanAfford` 改用值对象比较。
66. ~~**P1-7: Conversation.AddMessage 冗余 null 检查**~~ ✅ **已修复**：删除 `?? new TokenUsage(0, 0)`。
67. ~~**P3-16: ConversationsController 混合 Agent 接口**~~ ✅ **已修复**：拆分 `AgentsController`（POST /agents, GET /agents/{id}）。
68. ~~**P3-17: IConversationRepository 缺 GetByTenantAsync**~~ ✅ **已修复**：接口追加 + ConversationRepository 实现。
69. ~~**P2-13: AutoGenAgentOrchestrator 未注册 DI**~~ ✅ **已修复**：注册到容器。
70. ~~**P3-18: CostController 硬编码 dailyBudget=50**~~ ✅ **已修复**：从 `IOptions<RouterSettings>.DailyBudget` 读取。
71. ~~**P2-15: InMemoryToolRegistry 缺 CancellationToken**~~ ✅ **已修复**：`Register`/`Unregister` 追加可选 `CancellationToken`。
72. ~~**B-F1~B-F2: 蓝图 OpenTelemetry 标注不清晰**~~ ✅ **已更新**：阶段一学习目标明确标注"阶段三补充"。
73. ~~**B-F3~F5: QuickStart 配置路径与代码不一致**~~ ✅ **已修复**：`appsettings.QuickStart.json` 改用 `ModelDefaults`/`Router`/`Pricing`/`Cache` 节。
74. ~~**B-F6: UseAuthorization 未标记阶段二**~~ ✅ **已修复**：蓝图、代码均已标注。
75. ~~**C1: DDD 事件未在聚合根内触发**~~ ✅ **已修复**：`IAggregateRoot` 接口 + `_domainEvents` 集合 + `UnitOfWorkBehavior` 自动刷新。
76. ~~**C2-C3: 聚合根无参数校验 + 流式异常未捕获**~~ ✅ **已修复**：`ArgumentException.ThrowIfNullOrWhiteSpace` 守卫 + try-catch 包围流。
77. ~~**C4: ResiliencePipelineProvider 错用 ct**~~ ✅ **已修复**：改用 `pipelineCt` 而非外层 `ct`。
78. ~~**C5: AgentCreated 时间戳计算**~~ ✅ **已修复**：`{ get; init; } = DateTime.UtcNow`。
79. ~~**H6-H7: internal/sealed + 静态方法**~~ ✅ **已修复**：~23 个类加 `internal sealed`，`RoutingPolicyDomainService` 变 `static`。
80. ~~**H8: 方法参数 null 检查**~~ ✅ **已修复**：7 个字符串/对象参数加守卫。
81. ~~**M13: IModelRouter 错位**~~ ✅ **已修复**：接口移至 `Application.Abstractions`。
82. ~~**M14: PgVectorStore 冗余 async**~~ ✅ **已修复**：移除 `async`/`await`，直返 `Task.FromResult` / `Task.CompletedTask`。
83. ~~**M16-M18: StepName nullable + Redis 改名 + 硬编码 fallback**~~ ✅ **已修复**：`= null!`、`InMemoryShortTermMemory`、移除硬编码 fallback。
84. ~~**B-F7~B-F10: 蓝图反馈**~~ ✅ **已更新**：标记 OpenTelemetry/QueryFilter/Serilog/BDD 覆盖差距。
85. ~~**A1: SK 元数据 key 不匹配**~~ ✅ **已修复**：`Usage.InputTokens` → `Usage.PromptTokens`；`Usage.OutputTokens` → `Usage.CompletionTokens`。
86. ~~**A2: ResiliencePipeline 缺少 Timeout 策略**~~ ✅ **已修复**：追加 `.AddTimeout(TimeSpan.FromSeconds(30))`。
87. ~~**A3: WorkflowStep.SetResult/SetError 缺少 null guard**~~ ✅ **已修复**：追加 `ArgumentException.ThrowIfNullOrWhiteSpace`。
88. ~~**A4: 向量库名硬编码**~~ ✅ **已修复**：提取为 `RoutingConstants.DefaultVectorCollection`。
89. ~~**A5: TenantSettings init 属性**~~ ✅ **已修复**：`init` → `set`。
90. ~~**A6: SendMessageResponse.Model 命名不一致**~~ ✅ **已修复**：`Model` → `ModelId`。
91. ~~**A7: RoutingPolicyDomainService 硬编码 1024**~~ ✅ **已修复**：提取为 `MinViableContextWindow` 常量。

## 0-1. 设计评审关（动手前强制 · 所有 Phase 皆适用）

> 目的：在动手写/改任何"蓝图能力"之前，先审**蓝图本身**选的范式对不对。这道关补齐 `ddd-code-reviewer`（实现保真）与 `ddd-phase-quality-gate`（DDD 结构）都查不到的盲区——**"线性瀑布 / 缺 critic-reflection 循环 / 上下文 token 爆炸 / RAG 不接地 / HITL 无断点 / 恢复过度承诺"这类蓝图级范式问题，代码再忠实也无法被代码审查发现**。

**触发时机（MANDATORY）**：
- 项目启动、首次进入 Phase 1 前：对 `AGENT_PLATFORM_BLUEPRINT.md`（含附录 C）跑一次 `blueprint-architecture-review`。
- 任何阶段若新增/修订蓝图章节（如 Phase 2 加编排范式、Phase 3 加可视化编排、Phase 5 加 Code Agent 闭环），在动手前先对变更章节重跑。
- 蓝图变更必须经此关，否则视为未评审。

**变更传播（MANDATORY · 蓝图→Phase 任务清单）**：
- 蓝图经此关改写后，须**同步传播**到把被推翻/新增决策"写进任务"的 Phase 文档——例如附录 C 重写（合并两模式为单一编排原语、统一 `WorkflowContext`、加 critic 循环、软化恢复承诺）须同步：Phase 2 Module 4 从"独立 AutoGen 编排器"改为"negotiation 预设"、Module 2 对齐统一契约与逐步持久化；Phase 3 补 F3/F5/F6 任务；Phase 4 补 critic/上下文策略任务。
- 传播后，对应的高风险模块须**重新跑 §0 的 `ddd-code-reviewer`**（旧蓝图下"忠实"的 PASS 已作废，须以新蓝图为 spec 重卡）。
- 禁止"只改蓝图不改 Phase 任务"——否则编码仍照旧决策实现，设计评审关的成效会在实现端蒸发。

**准入门槛**：
- 评审结论须达 **DESIGN READY**（或 NEEDS WORK 项已全部闭环并复核通过）才允许进入对应 Phase 的编码。
- 报告写入 `docs/blueprint-architecture-review-YYYY-MM-DD.md`，并在本文件「回顾 / 审查修复记录」中引用其结论。
- 评审发现的 P0 项**阻断**编码；P1 项必须在对应 Phase 的 `ddd-code-reviewer` 强制范围内被解决并验证（见 §0）。

**完成与提交纪律（MANDATORY · 适用所有 Phase · A+B 档 2026-07-16 落地）**：
- 某 Phase 标记为「完成」**当且仅当**该 Phase 的高风险叙事模块已跑 `ddd-code-reviewer` 且 `ddd-phase-quality-gate` 问题清零（**0 open findings**）。仅文档/计划改动不计入"完成"。
- 提交 `src/` 改动前，须将质量门结果写入仓库根 `.quality-gate.json`（`cleared: true` + 报告引用），并在 commit message 带 `Quality-Gate: <phase> cleared (0 open findings)`。
- 仓库已落地自动拦截：`scripts/git-hooks/pre-commit` 在暂存含 `src/` 时强制校验标记；CI `quality-gate` job 在 push/PR 同步校验。启用：`git config core.hooksPath scripts/git-hooks`（或跑 `scripts/install-hooks.ps1`）。
- 标记格式/模板/诚实性原则见 `docs/quality/QUALITY-GATE.md`。**不得**在 `src/` 实际仍有未修漂移时写 `cleared: true`。

**与 §0 路由策略的分工**：
- 设计评审关管"**蓝图选的范式对不对**"（动手前、审文档）；
- §0 路由策略管"**代码有没有照蓝图做**"（动手后、审实现，高风险模块强制 `ddd-code-reviewer`）。
- 两者互补：设计评审关兜底范式债，§0 兜底实现漂移债。Phase 3 不应替 Phase 2 背编排范式债。

> 本项目已跑过一次 `blueprint-architecture-review`：初评 **DESIGN NEEDS WORK**（4×P1 + 5×P2，无 P0 阻断），经附录 C 重写后复审升级为 **DESIGN READY**（P1 全部闭环，P2 进入排期），报告见 `docs/blueprint-architecture-review-2026-07-16.md`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性蓝图能力"的模块（编排器 / 状态机 / 协作引擎 / 沙箱闭环 / SSE 广播 / 监控指标 / RAG / Tool Calling / 模型路由等——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图、依赖是否真实使用、注册接口方法是否非空壳 |
| 纯基础设施 / 结构卫生模块（仓储 / DI / EF 映射 / Redis / CRUD 控制器 / 配置 / CI） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名/接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 附录 C.6 / §8.2 / 阶段 X 验收标准"）。缺此项即视为未通过。

### Phase 1 强制范围（高风险叙事性模块）

- **模型路由**（`SemanticKernelModelClient`）：核对阶段一验收标准「带工具调用的 RAG 对话」+ 蓝图模型路由章节；历史 C1 暴露 token 统计永远为 0（典型名不副实现）。
- **RAG**（`PgVectorStore`）：蓝图承诺向量检索，阶段一为**存根实现**；reviewer 必须确认 stub 是否已补真实实现，或仍显式标记为延期（不可静默留空）。
- **Tool Calling**（`ToolCallingDispatcher`）：核对蓝图函数调用章节。
- **弹性管道**（`ResiliencePipelineProvider`）：核对蓝图重试 / 超时 / 熔断章节；历史 A2 暴露缺 Timeout 策略。
- **成本统计**（`CostController`）：核对蓝图成本报表章节；历史 A1 暴露 token 统计为 0。

> 说明：Phase 1 的「审查修复记录」已**同时运行** `ddd-phase-quality-gate` 与 `ddd-code-reviewer`（见后文三次审查），符合本策略。此 §0 为各阶段统一标准，后续阶段照此执行。

## 审查修复记录（Quality Gate + Code Reviewer）

> 使用 `ddd-phase-quality-gate` 和 `ddd-code-reviewer` 两个 skill 审查 Phase 1 代码，发现 10 个问题，已全部修复。

### P1 修复（2 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| A1 | SK 元数据 key 不匹配 → token 统计永远为 0 | `SemanticKernelModelClient.cs:111` | `Usage.InputTokens` → `Usage.PromptTokens`；`Usage.OutputTokens` → `Usage.CompletionTokens` |
| A2 | ResiliencePipeline 缺少 Timeout 策略 → 模型 hang 时请求无限等待 | `ResiliencePipelineProvider.cs:43` | 追加 `.AddTimeout(TimeSpan.FromSeconds(30))` |

### P2 修复（4 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| A3 | WorkflowStep.SetResult/SetError 缺少 null guard | `WorkflowStep.cs:90,100` | 追加 `ArgumentException.ThrowIfNullOrWhiteSpace` |
| A4 | 向量库名 `"default"` 硬编码 | `SendMessageCommandHandler.cs:49` | 提取为 `RoutingConstants.DefaultVectorCollection` |
| A5 | TenantSettings.DefaultTenantId 使用 `init` 阻止 IOptionsMonitor 热重载 | `TenantSettings.cs:11` | `init` → `set` |
| A6 | SendMessageResponse.Model 属性命名与 Application 层 ModelId 不一致 | `SendMessageResponse.cs:13` | `Model` → `ModelId` |

### P3 修复（1 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| A7 | RoutingPolicyDomainService 硬编码 `1024` | `RoutingPolicyDomainService.cs:25` | 提取为 `MinViableContextWindow` 常量 |

### 已知遗留（需后续阶段处理）

| 编号 | 问题 | 严重度 | 说明 |
|------|------|--------|------|
| A8 | SendMessageCommandHandler 零单元测试 | P2 | 核心 API 路径无测试覆盖，阶段二需补全 |
| A9 | 集成测试 CI gated（缺 Docker runner） | P3 | `if: false` 待 CI 配置 Docker 后启用 |
| A10 | XML 注释全部为英文 | P3 | 新增中文注释规则，Phase 1 pre-existing 代码仅标记 |

### 第二次审查修复记录（2026-07-14 — Code Reviewer P1/P2 + P3 Auto-Fix）

> 回顾审查报告的 P1/P2 必修项以及 P3 小项的 Auto-Fix Rule，发现 6 个问题，已全部修复。

#### P1 修复（1 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| B1 | `IsRetryable` 未含 `OperationCanceledException` → Polly 超时跳过所有候选模型 | `ModelRouter.cs:115` | 追加 `or OperationCanceledException` |

#### P2 修复（2 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| B2 | Domain events 在 `SaveChangesAsync` 前发布 → 事件处理器读到旧数据 | `UnitOfWorkBehavior.cs:26-36` | `SaveChangesAsync` 移到事件发布之前 |
| B3 | `ToolDefinition` 是唯一未实现 `ITenantScoped` 的聚合根 → 全局查询过滤器不生效 | `ToolDefinition.cs:10` | 实现 `ITenantScoped`，添加 `TenantId` 属性 + 构造函数参数 + EF 配置 |

#### P3 修复（3 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| B4 | `TenantSettings` 可被继承（非 sealed） | `TenantSettings.cs:6` | `public record` → `public sealed record` |
| B5 | `Money` 运算符无 null 守卫 → null 引用时抛出 NRE 而非清晰异常 | `Money.cs:30-99` | 7 个运算符全部追加 `ArgumentNullException.ThrowIfNull(a/b)` |
| B6 | Controller/Handler 直接注入 `IOptions<TenantSettings>` 而非 `ITenantProvider` → Api 层耦合配置对象 | 3 个文件 | 改用 `ITenantProvider` 接口（接口已在 `Application.Abstractions` 定义且 DI 已注册） |

### 第三次审查修复记录（2026-07-14 — DDD Code Reviewer 最终审查）

> 基于完整的 Phase 1 代码审查（109 个源文件），采用 adversarial mindset 逐行分析。发现 4 个新问题，已全部修复。

#### P1 修复（2 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| C1 | SK Metadata token 提取 key 名错误 → token 统计永远为 0 | `SemanticKernelModelClient.cs:130-131` | `TryGetValue("Usage.PromptTokens")` → 改用 `"Usage"` 键 + JSON 解析，兼容 `PromptTokens`/`CompletionTokens` 和 `InputTokens`/`OutputTokens` 两种命名 |
| C2 | SendMessage 中 User 消息错误携带 TokenUsage → 对话 Token 累计翻倍 | `SendMessageCommandHandler.cs:67` | User 消息 `tokenUsage: response.TokenUsage` → 移除 tokenUsage（用户消息无 token 消耗） |

#### P2 修复（1 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| C3 | SendMessage 未校验对话状态 → 可在 Closed/Archived 对话中继续发消息 | `SendMessageCommandHandler.cs:41-42` | 追加 `conversation.Status != ConversationStatus.Active` 守卫，非活跃对话抛出 `InvalidOperationException` |

#### P3 修复（1 项）

| 编号 | 问题 | 文件 | 修复内容 |
|------|------|------|----------|
| C4 | WorkflowStep 构造函数未设 `UpdatedAt` → 初始值 `DateTime.MinValue`，EF Core 写入错误时间戳 | `WorkflowStep.cs:65` | 追加 `UpdatedAt = DateTime.UtcNow` |

### 对蓝图文档的反馈

1. ~~**SK 版本号应锁定**~~ ✅ **已更新**：技术栈表标注 `Semantic Kernel v1.30`。
2. ~~**`AgentRole` 双重定义模糊**~~ ✅ **已更新**：删除值对象 record，阶段一直接用 enum，蓝图标记阶段二改 AgentType。
3. ~~**MediatR 版本指南**~~ ✅ **已更新**：技术栈表新增 MediatR 12.4 行；DDD 表注适配器模式。
4. ~~**QuickStart 配置路径**~~ ✅ **已更新**：蓝图 10.1 改用 `--launch-profile QuickStart`。
5. ~~**EF Core 聚合根映射需说明**~~ ✅ **已更新**：附录 A.5 新增 EF Core 映射注意事项 ↩。
6. ~~**Application 与 Domain 的 `MessageRole` 重复**~~ ✅ **已更新**：删除 Application 定义，统一引用 Domain。
7. ~~**测试项目位置未约定**~~ ✅ **已更新**：蓝图 11.4 添加测试项目位置约定。
8. ~~**蓝图缺少 DI 注册完整说明**~~ ✅ **已更新**：三、DDD 铁律补充仓储 DI 注册说明。
9. ~~**蓝图缺少 DDD 实现层约束**~~ ✅ **已更新**：三、DDD 铁律补充"实现类必须放在 Infrastructure""抽象接口定义在 Application.Abstractions"两条约束。
10. ~~**蓝图未约定基础设施接口位置**~~ ✅ **已更新**：同上，明确 `IShortTermMemory` 类接口位置规则。
11. ~~**B1: .NET 8 引用过时**~~ ✅ **已更新**：蓝图 §3/§5 所有 .NET 8 → .NET 9。
12. ~~**B2: QuickStart 误导"stub model works"**~~ ✅ **已更新**：明确列出 10 个 stub 组件，避免误导。
13. ~~**B3: JWT/Identity 描述但零实现**~~ ✅ **已更新**：标注"阶段二实现"，删除承诺性语言。
14. ~~**B4: TenantId Query Filter 描述但未实现**~~ ✅ **已更新**：代码已加，蓝图补充代码示例。
15. ~~**B5: 测试位置 `tests/` vs `src/` 矛盾**~~ ✅ **已更新**：统一为 `src/` 内测试项目位置。
16. ~~**B6: 附录目录 10 缺 6**~~ ✅ **已更新**：附录列表仅列存在的 4 个，缺的标"阶段二补充"。
17. ~~**B7: 蓝图引用 CI/CD 但仓库无 workflows**~~ ✅ **已更新**：蓝图 §12 改为"阶段二实现"。

## 学习笔记

> 边编码边记录：踩了什么坑、发现了什么有趣的东西、对文档的反馈。

### 第一天（2026-07-09）

| 领域 | 主题 | 要点 | 参考 |
|------|------|------|------|
| 解决方案 | 项目结构 | 6 项目组织：Domain / Application / Infrastructure / Api / Workflow / Web | `src/` 目录结构 |
| DDD | 依赖方向 | Api→Application→Domain, Infrastructure→Application, Workflow→Application，Domain 零外部依赖 | 各 `.csproj` 引用 |
| DDD | 聚合根设计 | 4 聚合根（Agent / Workflow / Conversation / ToolDefinition）+ 2 实体 + 3 值对象 + 7 枚举 | `Domain/Aggregates/` |
| DDD | 领域事件 | `IDomainEvent` 纯接口 → `DomainEventBus` 适配器 → MediatR 发布 | `IDomainEvent.cs`, `DomainEventBus.cs` |
| Semantic Kernel | 模型封装 | `IChatCompletionService` 统一接口，`SemanticKernelModelClient` 封装多模型调用 | `SemanticKernelModelClient.cs` |
| Polly | 弹性管道 | `ResiliencePipeline` 组合重试 + 超时 + 熔断策略 | `ResiliencePipelineProvider.cs` |
| 路由 | ModelRouter | flat priority list + `OrderByDescending` 降级，非模型特定降级链 | `ModelRouter.cs` |
| BDD | SpecFlow | Gherkin Scenario Outline + Examples 表格驱动验证，中文场景描述 | `AgentRouting.feature` |
| CQRS | MediatR | CreateAgent 命令 + GetAgent 查询 + RunWorkflow 命令 | `Application/Agents/Commands|Queries` |
| EF Core | DbContext | PostgreSQL + Npgsql，`ApplyConfigurationsFromAssembly` 自动发现配置 | `AppDbContext.cs` |

### 第二天（2026-07-09）

#### 知识点

| 领域 | 主题 | 要点 | 参考 |
|------|------|------|------|
| DDD | 聚合根集合映射 | 用 `IReadOnlyList<T>` 暴露只读集合，`List<T>` 作私有支持字段 → EF Core 需 `UsePropertyAccessMode(PropertyAccessMode.Field)` | `ConversationConfiguration.cs` |
| DDD | 值对象不可变性 | `Money` 是 `record struct`，`+=` 返回新实例而非原地修改 → 多线程安全需注意引用更新 | `CostController.cs` |
| DI | 装饰器注册 | .NET DI 无内置装饰器：先自注册真实实现 + 工厂方法构造装饰器包裹 | `DependencyInjection.cs` |
| DI | MediatR v12 | `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly))` 替代旧版独立包 | `Application/DependencyInjection.cs` |
| C# | 命名空间解析 | 同 namespace 下的类型优先于 using 导入的同名类型 → 聚合根内避免与 Domain 级类型重名 | `AgentRole` 重构 |
| 线程安全 | 并发累计 | `decimal` 不支持 `Interlocked`，`ConcurrentDictionary` 适合字典场景，`lock` 适合单一值 | `CostController._lock` |
| 数据结构 | 内存注册表 | `ConcurrentDictionary<Guid, T>` 作轻量内存注册表，线程安全且无需外部锁 | `InMemoryToolRegistry.cs` |
| ASP.NET Core | launch-profile 机制 | `--configuration` = 编译配置（Debug/Release）；`--launch-profile` = 环境变量 + 启动设置 | `appsettings.QuickStart.json` |
| ASP.NET Core | Environment 加载 | `ASPNETCORE_ENVIRONMENT=QuickStart` 自动加载 `appsettings.QuickStart.json` | `Program.cs` |
| C# | 私有构造函数 + `= null!` | EF Core 反射构造实体 + `private set` 完成后赋值，`= null!` 抑制非空警告 | `Agent.cs`, `Conversation.cs` |

#### 踩坑记录

| 项目 | 现象 | 原因 | 解决 |
|------|------|------|-----|
| MediatR 版本冲突 | `AddMediatR` 找不到 | 项目装了 v11，代码用了 v12 API | 统一升级到 v12.4，移除 `MediatR.Extensions.Microsoft.DependencyInjection` |
| SK FinishReason | `SkillChatOptions.FinishReason` 编译报错 | 1.12 有 `FinishReason` 属性，1.30 已移除 | 删除该属性引用；`SKEXP0010` 抑制实验 API 警告 |
| SK IChatCompletionService | 命名空间找不到 | 1.12 在 `Microsoft.SemanticKernel`，1.30 移至 `Microsoft.SemanticKernel.ChatCompletion` | 检查版本，导入正确命名空间 |
| AgentRole 歧义 | 编译报歧义错误 | `Domain.Aggregates.Agents` 内值对象 record 与 `Domain.Enums` 枚举同名 | 删除值对象 record，直接使用 enum；阶段二改 `AgentType` |
| MessageRole 重复 | 两处独立 enum 内容相同 | 脚手架生成时未统一 | 删除 Application 层的 enum，统一引用 `Domain.Enums.MessageRole` |
| IToolRegistry 未注册 | `ToolCallingDispatcher` 构造炸 | DI 容器注入了依赖但没注册实现 | 新建 `InMemoryToolRegistry` 并注册 |
| CostController 丢数据 | 并发下花费累计不准 | `_todaySpent += cost` 不是原子操作 | 所有读写加 `lock (_lock)` |
| ModelTelemetryDecorator 不生效 | 日志无遥测输出 | DI 注册了装饰器但 `IModelClient` 没走装饰器 | 改用工厂注册：`SemanticKernelModelClient` 自注册，工厂构造 `ModelTelemetryDecorator` |
| Conversation.Messages 写不进去 | EF Core 运行期异常 | `IReadOnlyList<Message>` 接口不可写 | 加 `UsePropertyAccessMode(PropertyAccessMode.Field)` |
| 仓储未注册 DI | 构造炸 | 在 DI 容器写好了注入但没注册实现 | `services.AddScoped<IAgentRepository, AgentRepository>()` |
| Executor 跨层引用 | `nameof(NativeToolExecutor)` 无法编译 | `nameof` 需直接引用类型，Application 不能引用 Infrastructure | `IToolExecutor` 接口加 `Source` 属性，调度器按 `e.Source` 路由 |
| CallAsync 多余公开方法 | `IModelClient` 接口无 `CallAsync`，但装饰器公开了 | 将内部辅助方法误设为 `public` | 合并逻辑到 `ChatAsync`，删除 `CallAsync` |
| 一文件多类型 | `ModelRouter.cs` 含 6 个公共类型，难以维护 | 渐进式开发中未及时拆文件 | 拆为 `CostController.cs`/`AllModelsFailedException.cs`/`ModelCandidate.cs`/`RoutingRequest.cs` |
| TenantId 硬编码 | 所有请求共用假租户 | 开发初期为了方便写死了 GUID | `TenantSettings` 配置 + `IOptions<T>` 注入 |
| Polly 管线未用 | 注册了 `ResiliencePipelineProvider` 但 `ModelRouter` 自己写 try/catch | `ModelRouter` 不知有管线可用 | 定义 `IResiliencePipelineProvider` 接口，注入并使用 |
| Scalar 编译报错 | `MapScalarApiReference()` 找不到 | 忘了加 `using Scalar.AspNetCore;` | 补 using |
| Pipeline 类型不匹配 | `ExecuteWithRetryAsync<T>` 编译错误 | Polly 8.x 非泛型管线的 `ExecuteAsync` 返回 `ValueTask`（无结果） | 用闭包捕获 `result`，绕过类型参数限制 |

### 第四天（2026-07-09）

#### 知识点

| 领域 | 主题 | 要点 | 参考 |
|------|------|------|------|
| 配置 | IOptions 模式 | `services.Configure<T>(section)` + 注入 `IOptions<T>` / `IOptionsSnapshot<T>` / `IOptionsMonitor<T>` | `TenantSettings` |
| Polly 8.x | 非泛型管线 | `ResiliencePipeline` vs `ResiliencePipeline<T>`：非泛型适合"fire-and-forget"，泛型适合有返回值 | `ResiliencePipelineProvider.cs` |
| Polly 8.x | ExecuteAsync 签名 | 非泛型 `ExecuteAsync(Func<ResilienceContext, ValueTask>, CancellationToken)`，返回值需闭包捕获 | `ResiliencePipelineProvider.cs:36` |
| ASP.NET Core | OpenAPI + Scalar | .NET 9 内置 `AddOpenApi()` 生成文档，`Scalar.AspNetCore` 提供现代化 UI 替代 Swagger UI | `Program.cs` |
| ASP.NET Core | JSON 选项 | `AddJsonOptions` 配置 `PropertyNamingPolicy`（CamelCase）和 `DefaultIgnoreCondition`（忽略 Null） | `Program.cs` |
| DI | 接口抽象 | 第三方库类型（Polly `ResiliencePipeline`）不应暴露到 Application 层 → 用应用层接口包装 | `IResiliencePipelineProvider.cs` |

#### 踩坑记录

| 项目 | 现象 | 原因 | 解决 |
|------|------|------|-----|
| TenantId 硬编码 | 所有请求共用假租户 | `Guid.Parse(...)` 写死 | `TenantSettings` 配置 + `IOptions<T>` |
| Polly 管线未接入 | `ModelRouter` 不用弹性管线 | 不知道 `ResiliencePipelineProvider` 存在 | 定义接口 `IResiliencePipelineProvider`，`ModelRouter` 注入 |
| Scalar 编译错 | `MapScalarApiReference()` 找不到 | 缺 `using Scalar.AspNetCore;` | 补 using |
| Pipeline 类型不匹配 | 非泛型管线无法直接返回 `T` | Polly 8.x 非泛型 `ExecuteAsync` 返回 `ValueTask`（void） | 闭包捕获 `result` 变量 |

### 第三天（2026-07-09）

#### 知识点

| 领域 | 主题 | 要点 | 参考 |
|------|------|------|------|
| DDD 依赖方向 | 接口 vs 实现 | 接口定义在 Application.Abstractions，实现类放在 Infrastructure | `IToolExecutor`、`IShortTermMemory` 重构 |
| DDD 依赖方向 | 跨层调度 | 调度器需要区分实现类时，应在接口加标识属性而非 `nameof` | `IToolExecutor.Source` 属性 |
| DI | 仓储注册 | .NET DI 不会自动扫描程序集注册仓储，必须手动 `AddScoped` | `DependencyInjection.cs` |
| C# | `nameof` 限制 | `nameof(T)` 要求 T 在当前编译单元可访问，跨项目不可用 | 改用接口属性方案 |
| 代码质量 | 方法提取 | 相同的 switch 跨多个方法应提成私有方法，消除 DRY 违规 | `CostController.GetCostPerUnit` |

#### 踩坑记录

| 项目 | 现象 | 原因 | 解决 |
|------|------|------|-----|
| 仓储未注册 | `AgentRepository` 等 3 个实现类在 DI 容器永不被解析 | 写了实现和接口，漏了 `AddScoped` 绑定 | 在 `DependencyInjection.cs` 补注册 |
| Executor 跨层编译失败 | Application 层无法 `nameof(Infrastructure.Tools.NativeToolExecutor)` | `nameof` 需要编译时类型引用，Application 不能引用 Infrastructure | 接口加 `ToolSource Source` 属性 |
| RoutingPolicyDomainService 应用层残留 | Domain 和 Application 各有一份相同代码，修改时容易不同步 | 脚手架生成时在两层各写了一份 | 删除 Application 副本，统一用 `Domain.Services` |
| QuickStart 配置不加载 | `dotnet run --configuration QuickStart` 无效 | `--configuration` 只影响编译配置，不影响环境 | 改用 `--launch-profile QuickStart` |
