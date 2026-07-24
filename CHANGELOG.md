# 变更日志

## v2.1 (2026-07-24)

### F2 · 登录与鉴权态一致性完成（feature-builder 全栈实跑）

把「前端 localStorage + Bearer」的脆弱鉴权态改为 **httpOnly + SameSite Cookie 承载 JWT**，并把登录密码从「形同虚设」改为 **PBKDF2 真实校验**（`dotnet test` 214/0，`node scripts/qa.mjs` 4/4）。

**后端：**
- 新增 `User` 聚合（`ITenantScoped` + `IAggregateRoot`）+ EF 迁移 `AddUserAggregate` + `UserConfiguration`（租户内邮箱唯一索引）+ `UserRepository`；`DatabaseInitializer` 幂等种子默认用户 `admin@acme.io / Admin@123456`（仅 Development/QuickStart 环境）
- `IPasswordHasher` + `Pbkdf2PasswordHasher`：PBKDF2-SHA256，10 万迭代，16B 盐，固定时间比对；格式 `$pbkdf2$<iter>$<saltB64>$<hashB64>`（零新依赖，用 `Rfc2898DeriveBytes`）
- `IJwtTokenService` / `JwtTokenService` 从 `DevLoginEndpoint` 抽取 token 发行逻辑
- `AuthEndpoints`：`POST /api/v1/auth/login`（验密→设 `ap_access_token` cookie：HttpOnly + SameSite=Lax + Secure=IsHttps + MaxAge=1h，返回 `{user}`）、`GET /api/v1/auth/me`（从 cookie 解析身份）、`POST /api/v1/auth/logout`（清 cookie）
- `AuthConfiguration` Smart 策略 `OnMessageReceived` 从 cookie 读 JWT；CORS 去 `AllowAnyOrigin` → `WithOrigins(Cors:AllowedOrigins)` + `AllowCredentials`

**前端：**
- `api.ts`：`axios.create({ withCredentials: true })`，移除 Bearer 注入与 localStorage；响应拦截器 401 派发 `auth:unauthorized` 事件
- `appStore` 去 localStorage，新增 `authBootstrapped` / `isDemo` / `bootstrapAuth()` / `loginReal()` / `loginDemo()` / `logout()`
- `LoginPage` 密码框 + 真实登录 + 「使用本地演示会话」；`ProtectedRoute` 等 bootstrap；`App` 监听 `auth:unauthorized` → 非 demo 跳 `/login`；SSE `fetch` / `EventSource` 改 `credentials:'include'`

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；新增 `AuthEndpointsTests` 5 例 + `Pbkdf2PasswordHasherTests` 5 例
- 分支 `feat/f2-login-auth-state`（commit `19af124` + `4af3fe9`），`.quality-gate.json` 推进 `f2-login-auth-state`

**已知残留（非阻断）：** 多租户登录按默认租户查用户（P2 waiver，目标后续「多租户登录」feature）；`Security:JwtSecretKey` 含 dev 兜底值（生产须环境变量覆盖）；种子默认密码生产须改

## v2.0 (2026-07-21)

### Phase 5 安全加固完成（launch-blocking）

把蓝图声称"第一优先级"、实际整层缺失的安全底座真实接线并通过二次评审闭环（`dotnet build` 0/0，`dotnet test` 103/103）。

**核心交付：**

- **认证双方案并存**：JWT Bearer + API-Key，用 `Smart` policy scheme（`ForwardDefaultSelector` 按请求头分发）作为默认方案；`ApiKeyAuthenticationHandler` 遵守 `NoResult()`（不适用）/ `Fail()`（无效）语义
- **真实多租户**：`TenantProvider` 从硬编码默认租户改为 per-request（Scoped）从 claim 解析 `tenant_id`，激活 `AppDbContext` 早已建好的 `HasQueryFilter` 隔离
- **RBAC**：`GetRoles` 从凭证取真实角色（Admin/Operator/Viewer），非恒 Admin
- **API Key 加密 + 生命周期**：`AesGcmEncryptor`（AES-256-GCM）+ `ApiKeyEncryptionService`；`ApiKey` 聚合 DB-backed（密文列）+ `IApiKeyRepository`；`Rotate/Revoke` + `ApiKeyExpiryJob`（每 6h 扫描过期）
- **提示注入防护**：`PromptInjectionMiddleware` + `PromptInjectionService`，正则收窄 + 负向测试
- **审计日志**：`AuditLog` 聚合 + `AuditActionType`，覆盖业务 4 handler + Key 三点位（KeyUsed/KeyRotation/KeyRevoked）
- **限流**：ASP.NET Core RateLimiter 按租户/Key 维度（`Security:RateLimitPerMinute`）

### 收尾排障（三个"编译过、运行炸"的坑）

- **认证无默认方案**：`AddAuthentication()` 空配置 → 访问 `[Authorize]` 抛 `No DefaultChallengeScheme found`。修复：加 `Smart` policy scheme
- **Swagger 无模拟登录**：缺 `AddSecurityDefinition` → 无 Authorize 按钮。修复：Swagger + Scalar 补 `Bearer` 定义；新增 `POST /api/dev/login`（`DevLoginEnabled` 门控、默认 false、返回裸 token）
- **`no such table: AgentConfigurations`**：`DatabaseInitializer` 用 `EnsureCreatedAsync()` 与 EF 迁移混用 → 旧 DB 缺 `AgentConfigurations`/`ApiKeys`/`AuditLogs`。修复：改用 `MigrateAsync()`；补落迁移 `Phase5ApiKeyIndex`；删旧 DB 迁移重建

### EF Core 迁移
- `Phase5ApiKeyStorage`：新增 `ApiKeys` + `AuditLogs` 表
- `Phase5ApiKeyIndex`：`ApiKeys` 索引由 `IX_ApiKeys_ExpiresAt` 改为 `IX_ApiKeys_IsActive_RevokedAt_ExpiresAt`

### 文档
- 新增学习笔记 [`docs/learning/10-phase5-security-learnings.md`](./docs/learning/10-phase5-security-learnings.md)（7 个安全知识点 + 3 个排障实录）
- `06-common-pitfalls.md` 扩充至 31 坑（新增认证/Swagger/迁移 5 坑）；同步导读、演进、决策日志、速记卡
- README 阶段路线 Phase 5 标记完成

> 说明：CHANGELOG 从 v1.6 直接跳到 v2.0——Phase 3（平台化）/Phase 4（知识接地加固）的详细条目见 `phases/phase-3-platformization.md`、`phases/phase-4-grounding.md` 与对应学习笔记。

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
