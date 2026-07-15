# 变更日志

## v1.6 (2026-07-15)

### Phase 2 多智能体工作流完成

**核心交付（9 个模块，70+ 源文件）：**

- **AgentType 值对象迁移**：`AgentRole` 枚举 → `AgentType` record 值对象，EF Core `OwnsOne` 映射，全套向后兼容
- **自研状态机引擎**：`WorkflowStateMachineEngine`，支持分支/重试（最多 3 次）/回滚，通过 `StateMachineSettings` 配置超时与重试策略
- **Redis 短期记忆**：`RedisShortTermMemory` 实现 `IShortTermMemory`，`IConnectionMultiplexer` Singleton 注册，连接失败降级到内存
- **AutoGen 多 Agent 协作**：6 种角色（需求→产品→架构→开发→测试→文档），`AutoGenAgentOrchestrator` 顺序管线编排
- **ExecutionLog 持久化**：`ExecutionLog` 聚合根 + `IExecutionLogRepository`，5 个 MediatR 领域事件驱动日志写入
- **可插拔数据库架构**：条件编译 `USE_SQLITE`/`USE_POSTGRESQL`，`DatabaseInitializer` 自动初始化和种子数据
- **CQRS 查询端点**：`GetAgents`、`GetConversations`、`GetExecutionLogs` 通过 MediatR Query/Handler
- **自定义 Agent 角色 CRUD**：`AgentRoleDefinition` 聚合根，`AgentRolesController` 完整 REST 端点
- **端到端集成**：完整管线需求 → 6 Agent → 输出，状态机持久化 + 恢复，ExecutionLog 全链路记录

### 新增 SpecFlow BDD 验收（5 个 .feature 文件）
- `AgentTypeMigration.feature`（3 场景）
- `WorkflowStateMachine.feature`（6 场景：正常流/重试/回滚/分支/并发/恢复）
- `MultiAgentPipeline.feature`（4+ 场景：完整管线/缺失 Agent/自定义角色/最大轮次）
- `ExecutionLog.feature`（5 场景：查询/过滤/分页）
- `CustomAgentRole.feature`（5 场景：CRUD + 验证）

### 新增配置类（6 个，全部通过 IOptions）
- `AutoGenSettings` — Agent 模型分配、最大轮次、终止条件
- `RedisSettings` — 连接字符串、过期秒数、Key 前缀
- `StateMachineSettings` — 最大重试、回滚超时、步骤超时
- `ExecutionLogSettings` — 保留天数、批量写入阈值、SSE 开关

### EF Core 迁移
- `Phase2MultiAgent` 迁移：8 张表（AgentType `OwnsOne`, ExecutionLog+Entries, WorkflowStep 等）
- 迁移可向前兼容（不破坏 Phase 1 已有表）

### 质量门审计
- **初次审计**（2026-07-15）：Gate Status PASS — 修复 P1×1（`IDatabaseInitializer` 移到 Application.Abstractions）、P3×3（sealed 修饰符、重复 Swagger 调用）
- **回归审计**（2026-07-17）：Gate Status PASS — 全 16 类审计通过，修复 P3×1（`AgentRoleDefinition` null! 注释）
- 最终验证：`dotnet build` 0 警告 0 错误，`dotnet test` 63/63 全部通过

### 蓝图同步
- `AGENT_PLATFORM_BLUEPRINT.md` Phase 2 任务清单已全部勾选
- `phases/phase-2-multi-agent-checklist.md` 完成审计记录更新

## v1.5 (2026-07-13)

### 变更
- **移除 Swagger/Scalar 环境限制**：`Program.cs` 取消 `if (app.Environment.IsDevelopment())` 条件，所有环境默认启用 API 文档
- **默认打开 Swagger UI**：`launchSettings.json` 3 个 profile 的 `launchUrl` 从 `openapi/v1.json` 改为 `swagger`
- **anchored-summary 同步**：移除 4 处 "Scalar (Development only)" 引用，更新为"所有环境默认启用"
- **phase-3-platformization 同步**：Swagger/Scalar 集成相关学习目标和任务项已勾选完成
- **phase-1-baseline-mvp 同步**：M1 修复记录补充"后续进一步移除环境限制"
- **AGENT_PLATFORM_BLUEPRINT 同步**：更新至 v1.5，追加修改日志
- **CHANGELOG 完善**：补充 v1.2~v1.5 缺失条目

## v1.4 (2026-07-10)

### Phase 1 全部代码优化完成

- UnitOfWorkBehavior 事件顺序修复（先分发领域事件，再 SaveChangesAsync）
- ConversationsController → MediatR Command/Handler（`CreateConversationCommand`、`SendMessageCommand`）
- CostController 接口抽象（`ICostController`，ModelRouter 通过接口引用）
- Db 凭据安全化（移除硬编码连接字符串，改为必填配置）
- Scalar 环境限制放宽（从 `IsDevelopment()` 改为 `IsProduction()` 才屏蔽）
- Conversation/Message UpdatedAt 修复（`set;` → `private set;`）
- 空守卫补全（7 个领域方法参数加 `ArgumentException.ThrowIfNullOrWhiteSpace`）
- using 清理（移除未使用的 import）

### 蓝图同步 (v1.4)
- QuickStart URL/cURL 修正（`--launch-profile QuickStart` + 正确 cURL 示例）
- Phase 1 清单已勾选
- 目录树补充 Conversations/ 和 SpecFlowTests
- 缺失 Abstractions 补全（`IResiliencePipelineProvider`、`TenantSettings` 等）
- Workflow 项目标记 Phase 2 骨架
- 删除 Aspirational Serilog 配置，代以 ILogger 现状描述
- 补充 OpenAI:Key / 环境变量文档

## v1.3 (2026-07-09)

### 补充 DDD 铁律
- 仓储 DI 注册说明（`IAgentRepository` 在 Domain 定义接口，Infrastructure 实现并注册）
- 实现类位置约束（所有实现必须放在 Infrastructure 层，不可在 Application 层）
- 接口定义位置约束（抽象接口定义在 `Application.Abstractions`，不可在 Infrastructure 层定义）

## v1.2 (2026-07-09)

### 版本锁定与约定完善
- 锁定 SK 版本为 1.30.0（技术栈选型表标注）
- 明确 MediatR v12+ DI 指南（`AddMediatR` 内置注册，无需独立包）
- 修正 QuickStart 启动命令（`--configuration` → `--launch-profile`）
- 添加测试项目位置约定（`src/` 目录下）
- 补充 EF Core 聚合根映射说明（附录 A.5）

## v1.1 (2026-07-01)

### 新增
- **Section 八：监控与运维**（补齐之前缺失的编号）—— 8.1~8.6 覆盖指标定义、埋点策略、Dashboard 设计、告警规则、日志采集、P0 性能目标
- **附录 C.8：Agent 角色可扩展性**—— 从 `AgentRole` 枚举到 `AgentType` record 值对象的改造方案，含现状分析、预留扩展空间、前后代码对比、联动改动清单、前端 UX 图
- **附录 G.8：前端架构详述**—— zustand 状态管理、TanStack Query API 层、React Router 路由、CanAccess 权限组件、React Flow 编辑器集成、完整 `src/` 目录结构
- **附录 H：部署与 DevOps**—— Docker Compose 开发环境、生产部署架构、CI/CD 流水线、环境配置管理、扩容策略、前端发布
- **附录 I：API 接口规范**—— 7 个资源域（认证/工作流/Agent/模型/对话/监控/管理），含 JSON 示例和 SSE 流式协议
- **Section 十一：编码约定**—— 命名规范表、Git 工作流、AI 编码约束提示词模板、测试约定、文档维护流程
- **Section 12：失败场景示例**—— 模型降级全链路日志输出、SQL 状态查询、人工恢复步骤
- **1.1 非功能目标**—— 可用性 99.9%、数据持久性 99.999%、并发租户 ≥ 100 等 P0 指标
- **10.1 5 分钟快速开始**—— SQLite + Stub 模式，无需 Docker 即可本地运行

### 重构
- **附录拆分**：9 个附录（3081 行）从主文档拆分为 `appendices/` 下独立 `.md` 文件
- **主文档瘦身**：从 ~3656 行减至 ~660 行，AI 加载速度提升 5x
- **9 个附录全部添加** `[← 返回主文档]` 链接
- **ToC 改为外部链接**：附录指向 `./appendices/xxx.md`

### 修复
- 章节编号跳号（缺八）已补齐
- C.8 AgentType 改造成本已同步到阶段二/三/四任务清单
- 项目定位更新为"6 种预置角色 + 自定义 AgentType"
- 8.6 段落末尾孤立 ``` 代码围栏已删除
- 附录 H `---` 前锚点标签丢失已恢复

### 元数据
- 主文档顶部添加版本号、最后更新日期、修改日志
- 附录 C 和 G 的子节（C.1~C.8 / G.1~G.8）添加 `<a name>` 锚点
- 附录索引添加阅读路线图（初次通读/按需查阅/常见场景）

---

## v1.0 (基线)

完整蓝图初版，包含：

- 项目定位、技术栈选型对照表（Python vs C# 匹配度）
- DDD 分层架构目录脚手架（6 个项目）
- BDD/TDD 工程化（SpecFlow + xUnit）
- 阶段一~四任务清单（基础 MVP → 多Agent → 平台化 → 前沿特性）
- 避坑清单（C# 做 AI 的 4 个短板 + 对策）
- 7 条关键设计原则
- 安全与鉴权（JWT / RBAC / 多租户 / Prompt 注入 / 沙箱逃逸 / 审计日志）
- Vibe Coding 使用说明
- 附录 A：核心聚合字段与状态枚举
- 附录 B：状态机引擎迁移方案（自研 → CoreWF）
- 附录 C：多 Agent 协作机制详解（C.1~C.7）
- 附录 D：多模型统一调用机制详解
- 附录 E：vLLM 定位与推理引擎选型
- 附录 F：能力扩展体系（Tool / Skill / MCP 三层架构）
- 附录 G：前端形态选型（Web / 桌面 App / 双形态）
