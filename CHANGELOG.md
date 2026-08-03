# 变更日志

## v2.14 (2026-07-31)

### F21 · 工作流触发器（Webhook / 定时 / Chat） 完成（feature-builder 全栈实跑，🔴高风险增量闭环）

为工作流补齐三种被动触发能力，全链路多租户隔离 + 审计，三道质量门全 PASS。

**核心改动：**
- **触发器聚合**：新增 `WorkflowTrigger`（`ITenantScoped`，每工作流至多一个 Webhook + 一个 Schedule，按 `TriggerType` 区分；`TriggerToken` 32 字节 URL-safe base64 不可猜、`Cron`/`Timezone`/`NextRunAt` 仅 Schedule）+ `ConversationWorkflowBinding`（会话→工作流多对多，Chat 触发器）。EF 迁移 `20260803014825_AddWorkflowTriggersAndBindings`（`Id` 均 `ValueGeneratedNever()` 避 GUID 陷阱；唯一索引 `(TenantId,WorkflowId,Type)` 防重复触发器）。
- **Webhook 触发器**：`POST /api/v1/webhooks/workflow/{token}` 匿名入口（受 `WebhookAnonymous` 限流，令牌即鉴权，未知/禁用令牌→404 不泄露存在性）；管理端点 `POST/DELETE {id}/triggers/webhook`（生成/启用幂等复用、禁用保留令牌）。
- **定时触发器**：`PUT {id}/triggers/schedule`（cron+IANA 时区，幂等 upsert；Cronos 计算 `NextRunAt`，空 cron→400）+ 后台 `WorkflowScheduler`（`BackgroundService`，30s 轮询跨租户扫描到期项，每触发器专属分布式锁防多实例重触发，先推进 `NextRunAt` 再运行避免失败死循环）。
- **Chat 触发器**：会话绑定/解绑/列表/触发端点；`TriggerWorkflowFromConversation` 三重租户校验（会话/工作流归属 + 绑定存在）后委托 `TriggerWorkflowCommand`（Chat 仅作信封/审计标签，`TriggerType.Chat`，不持久化 `WorkflowTrigger` 实体）。
- **调度防腐**：`TriggerWorkflowCommandHandler` 运行时注入租户（`ITenantContext` Scoped）、合并触发信封到 Context、`Running` 守卫防重入、运行后还原 Context 避免载荷污染工作流配置；`Reset()` 处理终态重跑。
- **前端**：`WorkflowTriggersDrawer`（Webhook 令牌展示/复制/禁用、Schedule cron/时区/下次运行、Chat 绑定数）+ `ConversationDetailPage` 工作流绑定 Drawer（绑定/运行/解绑）+ `types`/`api`/`locales` 全量对齐（i18n 对称）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f21-workflow-triggers`
- 后端 `dotnet test src/AgentPlatform.sln` **354 passed / 0 failed**（SpecFlow 41 / Arch 9 / Application 143 / Infrastructure 123 / Api 33 / Integration 5；含 F21 新增 App 层 18 例 + Api 契约 3 例 + **联调冒烟 3 例**：真实宿主 ASP.NET Core 管线跑通 Webhook/Schedule/Chat 全生命周期）
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit 全绿，i18n-symmetry 通过）
- 质量门闭环修复：Redis 分布式锁释放由无条件删除改为令牌 CAS（`Lua if GET==ARGV then DEL`，降级路径不持有真实锁），杜绝多实例 TTL 过期后误删他实例锁

## v2.13 (2026-07-30)

### F7 · 工作流版本管理 + 导入导出 完成（feature-builder 全栈实跑，🟢低风险增量；program 子项①）

把 DAG 画布 MVP 推向生产级平台能力的第一步：为工作流增加不可变版本快照、历史查看、回滚、删除，以及整工作流 JSON 导出/导入（导入恒为**新**工作流），全程多租户隔离 + 审计。

**核心改动：**
- **版本聚合**：新增 `WorkflowVersion`（Domain 聚合，`ITenantScoped` 不可变快照：Context + Nodes + Edges 序列化为 JSON）。EF 迁移 `20260730062346_AddWorkflowVersions`（`Id` `ValueGeneratedNever()` 避 GUID 陷阱；快照列 nvarchar(max)；非唯一 `(WorkflowId, VersionNumber)` 索引）。
- **快照机制**：`WorkflowGraphSnapshot` 记录（`FromWorkflow`/`ToJson`/`FromJson`/`ToReplaceGraphArgs`）；快照以原节点 Id 作 TempId，`Workflow.ReplaceGraph` 内部重映射保留图拓扑；损坏 JSON→`InvalidOperationException`。
- **7 个端点**：`POST {id}/versions`（存为版本，版本号=最新+1）、`GET {id}/versions`（分页列表）、`GET {id}/versions/{vid}`（详情）、`POST {id}/versions/{vid}/restore`（回滚，Running/Paused 抛 `WorkflowConflictException` 拒绝）、`DELETE {id}/versions/{vid}`（幂等删除）、`GET {id}/export`（导出 JSON）、`POST import`（导入为新工作流，经 `ReplaceGraph` 校验图结构）。写/回滚/删/导入限 `[Authorize(Roles="Admin,Operator")]`，读/导出/列表仅 `[Authorize]`。
- **审计**：新增 5 个 `AuditActionType`（CreateWorkflowVersion / RestoreWorkflowVersion / ImportWorkflow / ExportWorkflow / DeleteWorkflowVersion）；Export 为查询，已显式注入 `IAuditLogRepository`+`IUnitOfWork` 持久化审计（修复质量门发现的死代码）。
- **前端**：`WorkflowsPage` 版本历史 Drawer（存为版本/回滚 `modal.confirm`/删除 `Popconfirm`/导出下载 JSON Blob）+ `canManage` RBAC 门控；`WorkflowCanvasPage`「导入 JSON」按钮（读取文件→`importWorkflow`→跳转新工作流）。`types`/`api`/`locales` 全量对齐（i18n 对称，zh-CN 去字面 Agent）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f7-workflow-versioning`
- 后端 `dotnet test src/AgentPlatform.sln` **298 passed / 0 failed**（SpecFlow 41 / Arch 9 / Application 114 / Infrastructure 102 / Api 27 / Integration 5；含 F7 新增 `WorkflowGraphSnapshotTests` + `WorkflowsVersioning` handler 测试）
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit 全绿）
- 已知观察（非阻断）：`WorkflowVersion.CreatedBy` 恒 `null`（审计不记操作人，设计如此，前端已守卫）；版本号并发（`GetLatestVersionNumberAsync()+1` 无行锁，索引非唯一故不抛异常，设计项 G6）
- F7 其余子项（② 版本差异查看 / ③ 回滚预览 / ④ 版本标签 / ⑤ 定时快照 / ⑥ 版本权限 / ⑦ 跨工作流复制 / ⑧ 版本讨论）见 `features/workflow-platformization.md`，未在本轮实现

## v2.12 (2026-07-29)

### F19 · Agent Roles 内建标记 + 页面补全 + 分类合并（统一角色目录，DB 为准）完成（feature-builder 全栈实跑，🟡中风险）

把分裂的「`AgentType` 硬编码值对象」与「`AgentRoleDefinition` DB 表」两套角色分类合并为**一套以数据库为准的统一角色目录**，修复平台默认角色被错标"自定义"的 bug，并补全 `AgentRolesPage` 的编辑/删除与被引用计数能力。

**核心改动：**
- **统一目录（DB 为准）**：`AgentRoleDefinition` 表成为唯一权威；`AgentType` 值对象降级为内建目录的类型化镜像（`Predefined` code 与 `BuiltInRoleCatalog` 完全一致）；新增架构 parity 测试（`AgentRoleCatalogParityTests` 3 例）断言两者 code 集合相等，强制"DB 为准"，杜绝再次漂移。
- **内建标记**：`AgentRoleDefinition` 增 `IsBuiltIn`(bool) + EF 迁移 `AddAgentRoleIsBuiltIn`（`defaultValue:false`，存量行安全）；`DatabaseInitializer` 幂等对齐 7 个内建（缺失→插入、已存非内建→`MarkAsBuiltIn`）。
- **存量 Agent 数据修复（审查发现）**：设计原假设「存量 Agent `RoleCode` 已与新目录一致」不成立（旧码 architect/developer/tester/pm/tech-writer 整体不符）→ 在 `DatabaseInitializer` 新增 legacy→new 幂等映射（`IgnoreQueryFilters()` 全租户），防存量 Agent 游离于新目录之外。
- **引用计数**：`IAgentRepository.CountByRoleAsync(tenantId, roleCode)` + `AgentRoleSummary.AgentCount`，列表展示每角色被多少 Agent 引用。
- **编辑端点**：新增 `PUT /api/v1/agent-roles/{roleCode}` + `UpdateAgentRoleDefinitionCommand/Handler`（内建 `RoleCode` 锁、不可删）。
- **删除拦截**：`DeleteAgentRoleCommand` 重写为枚举结局（`Deleted`/`NotFound`/`BuiltInConflict`/`InUseConflict`）；内建→409、被引用→409、不存在→404、可用→204。
- **前端收口**：`AgentRolesPage` 删硬编码 `BUILT_IN_ROLES`、按 `IsBuiltIn` 分区、新建/编辑/删除模态 + RBAC + `agentCount` 展示；`AgentsPage` 默认 `roleCode` 由 `developer` 修正为 `development`；`types`/`api`/`locales` 对齐（i18n 对称 4 例，zh-CN 去字面 "Agent"）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f19-agent-roles-unified`
- 后端 `dotnet test src/AgentPlatform.sln` **287 passed / 0 failed**（SpecFlow 41 / Arch 9 / Application 103 / Api 27 / Integration 5 / Infrastructure 102；含 F19 新增 parity 3 + handler 7 + Api 集成 7）
- 前端 `tsc --noEmit` **0 error** + `vitest` **38/38 green** + `vite build` 通过
- 审查修复 P2×2：`AgentsController` 默认 `RoleCode ?? "developer"`→`"development"`；`DatabaseInitializer` 补 legacy→new 映射
- 已知环境限制：`qa.mjs` 的 `lint` 闸门因 `package.json` 未声明 `@eslint/js` 等依赖（orphaned `eslint.config.js`）恒失败，非 F19 引入；typecheck/build/unit 三实质闸门全绿

**v2.11 (2026-07-30)**

### F18 · Dashboard 图表充实（运行分析看板）完成（feature-builder 全栈实跑，🟡中风险）

把仅 4 个计数卡的 Dashboard 升级为运行分析看板（KPI 卡 + 时间序列/分布图），对标 Dify/LangSmith/Flowise/n8n/Coze。

**核心改动：**
- **后端新增聚合端点**：`GET /api/v1/analytics/summary` `[Authorize]`（沿用 Dashboard 现有「已认证即可读」可见性，tenant-scoped via `ITenantProvider`）。单一 `DashboardSummaryDto` 一次返回全部图表数据（KPIs：活跃智能体/活跃工作流/总执行数/成功率/总 Token/平均延迟 + 日桶 ExecutionsByDay/TokenByDay/ConversationsByDay/LatencyByDay + TopWorkflows），避免 N 请求。Handler 取区间内租户原始行**应用层按日桶聚合**（v1；留 SQL `GROUP BY` 下沉余地）。含 `from>to`→400、`范围>366 天`→400 输入边界。
- **仓储扩展**：`IExecutionLogRepository`/`IConversationRepository` 新增日期范围重载（`internal sealed` 实现；`ExecutionLog` 查询 `Include(Entries)` 保证延迟聚合有效），无 EF 迁移（纯查询端点）。
- **前端图表化**：引入 `recharts@^2.15.4`（设计 D4 文档备选，React 19 兼容更稳、包体更轻）；`DashboardPage` 重写为 `Segmented` 7/14/30 天范围选择器 + 6 KPI 卡 + 6 图（执行趋势堆叠柱/成功率折线/Token 面积/会话量柱/平均延迟折线/Top 工作流横向柱），空态复用 `Empty`、错误复用 `ErrorState`；`api.ts` 加 `getDashboardSummary`、`types/index.ts` 对齐 `DashboardSummary` 系列（camelCase 与后端一致）；`locales` zh-CN/en-US 新增 `pages.dashboard` 图表键严格镜像（对称性测试通过）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f18-dashboard-charts`
- 后端 `dotnet test src/AgentPlatform.sln` **270 passed / 0 failed**（SpecFlow 41 / Arch 6 / Application 96 / Infrastructure 102 / Api 20 / Integration 5；含 F18 新增 `GetDashboardSummaryQueryHandlerTests` 6 例 + `EndpointContractTests` 集成 2 例）
- 前端 `tsc --noEmit` **0 error** + `vitest` **38/38 green**（含 i18n 对称 4 项）+ `vite build` 通过
- 设计偏离：图表库选用 **recharts**（设计默认 `@ant-design/plots` 的文档备选），6 图 + 6 KPI 卡与设计的图表集合完全一致；D2/D3 默认采用标签 `t()` 化 + 已认证即可读

**v2.10 (2026-07-29)**

### F17 · AgentConfiguration 实例化联动（模板库真正被消费）完成（feature-builder 全栈实跑，🟡中风险）

把「版本化 YAML 定义库孤岛」变为真正被前端消费与实例化的「Agent 定义/模板库」：定义可被读取为结构化模板、可被用来一键新建 Agent、并打通管理端 CRUD。

**核心改动：**
- **模板读取端点（后端新增）**：`GET /api/v1/agent-configurations/{id}/template` `[Authorize(Roles="Admin")]`，复用既有 `IYamlConfigurationParser`（YamlDotNet，UnderscoredNamingConvention）在服务端把 YAML 解析为结构化 `ConfigurationAgentTemplate`（`agent_role`/`system_prompt`/`model.{provider,name,api_url}` → 对应字段；缺字段留 `null`、畸形 YAML 降级为仅元数据不报错）；handler 显式比对 `TenantId` 做租户隔离（跨租户返回 404 而非 403，避免泄漏存在性）。
- **D1 溯源（可选）**：`CreateAgentCommand` 新增可选 `Guid? ConfigurationId`，`CreateAgentCommandHandler` 注入 `IAgentConfigurationRepository` best-effort 加载定义，并把「from configuration X vY」写入审计日志；加载失败**绝不**阻断创建（无 EF 迁移）。
- **前端 AgentConfigurationsPage 完整 CRUD**：新建/编辑 Modal（name / description / agentTypeCode 取自 `getAgentRoles()` / yamlContent TextArea）；每行 `⋯` 编辑 + `Popconfirm` 删除，均 Admin 门控；移除与「我的凭据」重复的凭据 tab；抽屉明细改为拉 `GET {id}` 详情取 `yamlContent`（列表 summary 不含 `yamlContent`）。
- **AgentsPage「基于模板新建」**：弹窗列定义（Active 优先）→ 选中 `getAgentConfigurationTemplate` 结构化预填创建表单 → `createAgent` 透传 `configurationId`；模板模型不在平台目录时注入合成目录项避免静默丢 provider。
- **RBAC 收敛**：`AppLayout` 将 Configurations 菜单收敛为 Admin 仅见（与后端 `[Authorize(Roles="Admin")]` 对齐）。
- **契约对齐**：`api.ts` 补齐 5 个方法、`types/index.ts` 对齐 3 个新类型（`AgentConfiguration` 漂移修正 `agentTypeCode`/`status`/`updatedAt` 对齐后端 camelCase）；`i18n` zh-CN/en-US 严格镜像（对称性测试通过）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f17-agent-config-instantiation`
- 后端 `dotnet test src/AgentPlatform.sln` **260 passed / 0 failed**（SpecFlow 41 / Arch 6 / Application 90 / Infrastructure 102 / Api 18 / Integration 5；含 F17 新增 `GetConfigurationTemplateQueryHandlerTests` 5 例 + `InvalidYamlExceptionHandlerTests` 2 例）；前端 `tsc --noEmit` **0 error** + `vitest` **38/38 green**（含 i18n 对称 4 项）+ `vite build` 通过
- 设计偏离：设计文档原拟新增 `AgentConfigurationYamlParser`，经核验 `IYamlConfigurationParser` 已存在，改为复用（无新增解析类）

**HOTFIX（同分支，提交 19766a7）**：修复「新增配置」`POST /api/v1/agent-configurations` 返回 500
- 根因①：请求记录 `CreateAgentConfigurationRequest` / `UpdateAgentConfigurationRequest` / `CreateAgentRoleRequest` 的校验特性写成 `[property: ...]` 加在记录主构造器位置参数上 → 触发 ASP.NET Core 模型验证 `ThrowIfRecordTypeHasValidationOnProperties` 抛 `InvalidOperationException` → 500（自 Phase 3 起所有经 MVC 绑定的创建接口受影响，F17 前端首次触达才暴露；后端单测走 MediatR 不经 MVC 绑定故一直未发现）。修复=校验特性直接加在位置参数（`[Required]`/`[StringLength]`，去掉 `[property:]`），对齐 `Models/*.cs` 既有正确写法；一并修 `CreateAgentRoleRequest`。
- 根因②：即便绑定通过，命令处理器对非法 YAML 抛 `ArgumentException` 仍变 500。修复=新建 `InvalidYamlException`（放 Application 层避免反向依赖 Api 致循环引用），处理器改抛它；新增 `InvalidYamlExceptionHandler : IExceptionHandler`（沿用 `UnsupportedContentTypeExceptionHandler` 模式）映射为 **400 Bad Request**。
- 实证：合法 YAML→200、非法 YAML→400、AgentRole 创建→200；`Api.Tests` 16→18。

**UI 交互修复（同分支，提交 bfe4426）**：
- AgentConfigurationsPage 编辑时 Drawer 与 Modal 同时打开 → `openCreate`/`openEdit` 开头 `setDrawerOpen(false)`，卡片内操作区 `onClick stopPropagation` 防冒泡到 `onItemClick`→`openDrawer`
- ConversationDetailPage 缺返回按钮 → 标题左新增 `←` 按钮 `navigate('/conversations')`（loading 态同步）；`PageHeader.title` 类型 `string`→`React.ReactNode`（无破坏性）

**已知残留（非阻断）：**
- 后端未配真实 LLM key 时，会话消息走 Stub 模拟回复（属预期）
- 模板端点仅 Admin 可见，与 Configurations 菜单 Admin-only 一致；终端用户经 AgentsPage「基于模板新建」消费，无需直接调端点

**分支：** `feat/f17-agent-config-instantiation`（未 push；含 6cabfbb / 19766a7 / bfe4426 三个提交）

## v2.9 (2026-07-29)

### F16 · 列表统一改为卡片（Card）形式展示完成（feature-builder 纯前端实跑，🟡中风险）

把 9 个实体列表页的 Antd `<Table>` 统一替换为响应式卡片网格，提升可视性与点击目标，对齐现代 Agent 平台（Dify/Coze）的卡片流。

**核心改动：**
- 新增通用组件 `components/EntityCardGrid.tsx`：统一「网格 + `Skeleton` 加载骨架 + `Empty` 空态 + 响应式列（normal 大屏 4 列 `lg=6` / compact 大屏 3 列 `lg=8`）+ `onItemClick` + `rowKey` + `density`」。
- 9 个列表页改造为卡片：`AgentsPage` / `AgentConfigurationsPage`(configsTab) / `WorkflowsPage` / `ConversationsPage` / `KnowledgeBasesPage` / `CredentialManager`(凭据) / `ApiKeysPage` / `ExecutionLogsPage`(compact) / `AgentRolesPage`(内置/自定义两网格)。各页用 `renderCard(item)` 提供单卡（标题/摘要/状态 Tag/操作），保留搜索/筛选栏、空态、加载态、分页（`Pagination` 复用 `skip/take/totalCount`，筛选切换复位 `page=1`）。
- 与 F15 i18n 协同：卡片内静态文案（空态/状态词/列标题）全走 `t()`，无硬编码用户串。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f16-card-layout`
- 审查修复 P0：`EntityCardGrid` 整卡 `onItemClick` 与卡内交互子元素（按钮/链接/输入）点击冒泡冲突 → 改为安全默认，命中 `button/a/input/select/textarea/[role=button]/[data-no-card-click]` 即拦截整卡跳转，避免「点删除又顺带导航」双重动作
- 前端 `tsc --noEmit` **0 error** + `vitest` **38/38 green**（含新增 `EntityCardGrid` 7 项单测 + `AgentsPage.contract.test.tsx` 字段映射契约更新）+ `vite build` 通过
- 模型一致性：无后端契约变更；纯前端渲染层改造

**已知残留（非阻断）：**
- 详情内子表（`ExecutionLogDetail` step entries / `KnowledgeBaseDetail` 文档列表 / `WorkflowDetail` Steps，按 D2 保留 `<Table>`）
- `ResearchPage` 任务流（非实体列表，故意排除）沿用旧形态
- `AgentConfigurationsPage` 与 F17、`AgentRolesPage` 与 F19 强耦合，F16 不改其写路径，由 F17/F19 收口

**分支：** `feat/f16-card-layout`

## v2.8 (2026-07-28)

### F15 · 多语言国际化 i18n（中文 + 英文）完成（feature-builder 纯前端实跑，🟡中风险）

引入 `i18next` + `react-i18next`，全站 UI 框架级文案支持中/英双语切换，顶栏「中文 / English」一键切换并持久化到 localStorage（默认 zh-CN），Antd `ConfigProvider` 与 `dayjs` 区域随语言联动。

**核心改动：**
- 新增 `src/locales/`：`index.ts` 初始化（默认 zh-CN、回退 zh-CN、读 `localStorage('app-locale')`）、`zh-CN.ts`、`en-US.ts`、`config.ts`（`SUPPORTED_LOCALES`/`DEFAULT_LOCALE`/`STORAGE_KEY`）。
- `en-US.ts` 以 `Resources = typeof zhCN` 类型约束保证两套结构镜像；`src/__tests__/i18n-symmetry.test.ts` 运行时扁平 key 对称测试兜底防漏翻。
- 新增 `components/LanguageSwitcher.tsx`（顶栏右上角 `Segmented`，切 `i18n.changeLanguage` + 持久化 + 触发 Antd/dayjs 区域联动）。
- `App.tsx` 顶层 `ConfigProvider locale` 与 `dayjs.locale` 随 `i18n` `languageChanged` 事件同步（初始语言由 `resolveInitialLocale` 解析）。
- 全站页面/组件 UI 文案 `t()` 化：导航菜单、登录页、各页标题与主按钮、表单标签、`Empty`/`ErrorState`/`message.*`、表格列头与状态标签；领域数据（用户填的 agent/workflow 描述、节点配置示例 prompt、`检索失败` 等后端逐字匹配串）按 D4 不翻。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f15-i18n`
- 审查修复：P1 `common.total` 双包缺失导致分页 `showTotal` 泄露原始键串→补键；P2 模块级 `columns` 在组件外调用 `t()` 触发 TS2304→改组件内工厂；P2/P3 多处漏翻硬编码 UI 串→统一 `t()`；P3 `en-US.ts` 重写对齐 + 新增 `config.test.ts` 4 项（locale 解析/持久化）
- 前端 `tsc --noEmit` **0 error** + `vitest` **30/30 green**（10 测试文件）+ `vite build` 通过
- 模型一致性：无后端契约变更；纯前端改造

**已知残留（非阻断）：**
- `codebase-optimizer` P3：36 个未引用 i18n key 已 waiver（antd 重叠词由 `ConfigProvider` 本地化、`errors.*` 预留 D1 后端错误本地化、`empty.*` 预留 Empty 描述）
- `@xyflow/react` 画布右键菜单等第三方内置中文未纳入 i18n（D4 已知残留，v1 不处理）

**分支：** `feat/f15-i18n`

## v2.7 (2026-07-28)

### F14 · 供应商模型发现（填 Key + Base URL 后拉取可访问模型清单）完成（feature-builder 全栈实跑，🔴高风险）

用户在「我的凭据」/Agent 配置页填 API Key + Base URL + 选 Provider 后，点「拉取模型」即可从该 provider 账户（OpenAI 兼容 `GET /v1/models`）拉回所有可访问模型，以下拉供选择，免去手填模型名易错的问题。

**核心改动：**
- **后端发现服务**：新增 `IProviderModelDiscovery`（Application.Abstractions 接口）+ `ProviderModelInfo` record + `ProviderModelDiscoveryException`（领域友好异常，携带可直接回传客户端的 400 中文原因，绝不泄露密钥）+ `ProviderModelDiscovery`（Infrastructure.Models，真实 `HttpClient` 出站，复用 `SerpApiSearchProvider` 的 `IHttpClientFactory` 模式，无 stub）。
- **端点**：`TenantCredentialsController` 新增 `POST discover-models`（RBAC `Admin,Operator`，只读探测、无落库、无密钥出 API 体）；`DiscoverModelsRequest`（provider / baseUrl / apiKey）。默认 base：OpenAI/DeepSeek 内置、Custom/VLLM 须显式填。
- **DI 注册**：`IProviderModelDiscovery` 注册 Scoped 单实现，控制器注入消费；无 EF 迁移。
- **前端契约**：`types/index.ts` 加 `ProviderModelInfo`、`api.ts` 加 `discoverProviderModels`；`CredentialForm` 模型类 `Model Name` 改 `AutoComplete`（允许自定义）+「拉取模型」按钮（loading / 错误提示 / edit 模式留空 Key 禁用并用 `Tooltip` 提示先填 Key）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f14-model-discovery`
- 审查修复 P1：`ProviderModelDiscovery` 原 `response.Content.ReadAsStringAsync` 位于 `try` 之外，若 15s 超时发生在读取响应体阶段会抛未捕获 `OperationCanceledException` → 500，已移入 `try` 并用 `using var response` 全程受请求级超时保护，超时统一映射为友好 400
- 审查修复 P2：`CredentialForm`「拉取模型」disabled `Button` 用 `title` 提示禁用原因但 antd v5 吞掉 hover 导致提示不可见，已用 `Tooltip` 包裹使其 hover 显示（满足 D1「按钮提示先填 Key」）
- `dotnet test src/AgentPlatform.sln` **255 passed / 0 failed**（含 F14 新增 11 例 ProviderModelDiscovery 单测，覆盖 URL/解析/401/404/空 data/缺 owned_by 等）；前端 `tsc --noEmit` **0 error** + `vite build` 通过
- 模型一致性：后端 camelCase 序列化 `{id, ownedBy}`、前端对应 `{id, ownedBy}`

**已知残留（非阻断）：**
- e2e 浏览器联动（Playwright/Edge）本沙箱未跑，单测已覆盖真实 HTTP 探测路径（StubHttpMessageHandler 验证 GET+Bearer+URL）
- SSRF 域名白名单不在本范围（D4），Admin 专用可接受

**分支：** `feat/f14-model-discovery`

## v2.6 (2026-07-27)

### F13 · 多租户凭据配置（模型 + 搜索，BYO-Key + 平台内置）完成（feature-builder 全栈实跑，🔴高风险）

补齐平台多租户化的最后一环——外部 API 凭据层租户隔离（模型 LLM key + Research 用 SerpApi 搜索 key 同构处理）。

**核心改动：**
- **聚合与落库加密**：新增 `TenantCredentialSetting` 聚合（`ITenantScoped` → `HasQueryFilter` 租户隔离；`Id` 显式 `ValueGeneratedNever`）+ `CredentialCategory` 枚举（Model/Search）+ `ITenantCredentialSettingRepository` + EF 迁移 `AddTenantCredentialSetting`。密钥复用 `IApiKeyEncryptionService`（AES-256-GCM），落库仅存密文 `EncryptedApiKey` + `ApiKeyPrefix`，明文不入 DB/不出 API/不进日志。
- **per-tenant 解析链路**：新增 `ITenantCredentialResolver`（按 `tenantId+category` 解析 + `IMemoryCache` 缓存密文实体 + `PUT` 即时失效）、`ITenantModelClientResolver`（解密后 `SemanticKernelModelClient.CreateForTenant` 构建租户模型客户端）、`IPlatformModelProvider`（运营方 `RouterSettings.Candidates` 平台模型）。`ModelRouter` 改造为合并平台 ∪ 租户候选；`SerpApiSearchProvider` 改为运行时按租户解析 key（BYO key 绕过平台配额，无则回退平台默认，均无则明确提示配 key）。
- **配额（B 防滥用）**：`ICostController` 扩展为租户键控（`PerTenantDailyBudget` 模型 / `PerTenantDailySearchQuota` 搜索）；BYO-Key 不受限。
- **端点与前端**：`TenantCredentialsController`（`GET/PUT /api/v1/tenant/credentials?category=Model|Search`，RBAC `Admin,Operator`，GET 返回掩码 `••••`+prefix，未配置 204）+ `PlatformModelsController`（`GET /api/v1/models`，平台 ∪ 租户 BYO，仅暴露标识不含密钥）。前端 Agent 配置页内嵌 `Tabs: 模型 + 搜索` 凭据配置（`Input.Password` 掩码 + provider Select + 保存）；`types/index.ts`+`api.ts` 补齐。
- **S4 收尾（模型下拉接线）**：Agent 创建页（Admin 专属「+ 新建 Agent」Modal，含角色 + 模型下拉，模型选项来自 `GET /api/v1/models`，选中的 `modelId`→`ModelName`、provider→`ModelProvider`）与会话详情页（顶栏「选择模型」下拉，分组「平台模型 / 我的模型」，选中值经 `sendMessage(model=modelId)` 透传为 `PreferredModel` 路由）均已接 `GET /api/v1/models`；`appStore` 新增 `userRole` 用于 Admin 按钮门控。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f13-multi-tenant-credentials`
- 审查修复 P0：`TenantCredentialsController.Put` 原直接写仓储但未提交 `IUnitOfWork.SaveChangesAsync`（本控制器不走 MediatR 命令、无 `UnitOfWorkBehavior` 自动提交），导致凭据永不落库——已注入 `IUnitOfWork` 显式提交，行为与命令处理器一致；新增 EF 集成测试锁定落库 + 租户隔离 + upsert 不重复行
- `dotnet test src/AgentPlatform.sln` **244 passed / 0 failed**（含 F13 新增 EF 集成测试、resolver/search BYO 单测）；前端 `tsc --noEmit` **0 error**
- 模型一致性：后端 camelCase 序列化、枚举 int，前端 `CredentialCategory` 常量对象一一对应

**已知残留（非阻断）：**
- ~~S4 模型下拉接 `GET /api/v1/models` 后端已就绪（返回 platform ∪ BYO），Agent/会话创建页模型下拉接线为后续小步~~ → **已完成**（见上方「S4 收尾」）。
- `appsettings.json` 因严格 JSON 不容注释，配额语义改在 `features/model-config.md` §3.6 文档化

**分支：** `feat/f13-multi-tenant-credentials`

## v2.5 (2026-07-24)

### F6 · Research Agent 联网多步调研完成（feature-builder 全栈实跑，🔴高风险）

把「开放问题 → 多步联网检索 → 结构化报告」做成一等能力（Research Agent）。原蓝图阶段四 TODO「Research Agent」落地。

**核心改动：**
- **真实联网检索**：新增 `ISearchProvider` + `SerpApiSearchProvider`，对 `serpapi.com/search.json` 发起**真实 GET** 并解析 `organic_results`（标题/URL/摘要）；缺 key / 非 2xx / 超时 / 传输错误 → `Success=false` + 真实 `ErrorMessage`，**绝不伪造成功**。密钥走 `SearchSettings` / 环境变量 `Search__SerpApiKey`，**不落库**（不复用 `ToolDefinition.EndpointUrl`）
- **多步链真实串联**：`ResearchCommand` + `ResearchCommandHandler`（注入 `IModelClient` / `ISearchProvider` / `ITokenCounter` / `IOptions<StateMachineSettings>` / `IOptions<SearchSettings>`）按 `plan → search×N → synthesize` 自驱循环；`Sources` 按 URL 去重累积；多轮发现超 `MaxSummaryTokens`(8000) 预算截断。LLM 规划/综合均经注入 `IModelClient`（生产真实 SemanticKernel，测试 stub）
- **SSE 流式端点**：`ResearchController`（`POST /api/v1/research`，`[Authorize]` 全认证租户用户）以 `text/event-stream` 流式写出 `ResearchProgressEvent`（`Plan → SearchStart/SearchDone×N → Synthesize → Report`，异常为 `Error`+空 `Report`），终端 `event: done` 收尾；序列化 camelCase、事件 `Type` 整型枚举（0–5）
- **配置**：新增 `SearchSettings`（`Application.Abstractions`）+ `appsettings.json` 的 `Search` 节（`Provider`/`SerpApiKey`/`BaseUrl`/`TimeoutSeconds`/`DefaultMaxResults`）；`DI` 按 `Provider` 选择实现（未知值启动报错）+ `AddHttpClient()`
- **前端**：新增 `ResearchPage`（提问 + 实时 Timeline 进度 + 结构化报告渲染：来源卡片 / 答案 / 分节）、`types/index.ts` 的 Research 类型、`api.ts` 的 `runResearch`（fetch + `credentials:'include'` 逐帧解析 SSE）、`App.tsx` 路由 `/research`、`AppLayout.tsx` 菜单项

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f6-research-agent`
- 已核对**真实副作用验收**：`SerpApiSearchProviderTests`（StubHttpMessageHandler 模拟 SerpAPI）覆盖真实 GET 构造 + `organic_results` 解析 + 缺 key/非 2xx/超时/传输错误；`ResearchCommandHandlerTests` 覆盖搜索调用 N 次 / `Sources` 去重 / `Sections` 非空 / 计划·综合失败精准回打
- `dotnet test src/AgentPlatform.sln` **238 passed / 0 failed**（含 F6 新增 8 例）；前端 `tsc --noEmit` **0 error** + `vite build` 通过
- 模型一致性：后端 camelCase、事件 `Type` 整型枚举，前端 `ResearchEventTypeValue` 常量对象一一对应

**已知残留（非阻断）：**
- `SerpApiKey` 为空时各查询失败但报告仍基于已规划内容生成（优雅降级）
- 真实 SerpApi 端到端需生产密钥（单测用 mock transport 覆盖真实 HTTP 路径）
- 报告正文体为 Markdown 文本前端以 `pre-wrap` 渲染（未引 `react-markdown` 依赖，结构化字段 `Sources`/`Answer`/`Sections` 已拆分）

**分支：** `feat/f6-research-agent`

## v2.4 (2026-07-24)

### F5 · 行动层落地（Agent 真正能做事）完成（feature-builder 全栈实跑，🔴高风险）

把原先**空心**的执行层变成**真实副作用**：调工具、跑代码均产生真实外部效果，而非伪造成功。

**核心改动：**
- **A1 原生工具真实 HTTP**：`NativeToolExecutor` 从「返回假成功」改为对 `ToolDefinition.EndpointUrl` 发起真实 HTTP 调用；方法解析（默认 POST、无参走 GET、显式 `httpMethod` 覆盖）、2xx→成功回体、非 2xx→精准回打真实状态、超时→`工具调用超时`。符合 Phase 6 critic 范式（失败精准回打）
- **A2 代码沙箱真实进程**：新增 `ProcessCodeSandbox`（`System.Diagnostics.Process` 拉起 python / node 真实运行），捕获真实 stdout / stderr / ExitCode / 超时杀进程，替代原伪造成功的 `DockerCodeSandbox`（后者改为显式抛异常，消除静默假成功）。Docker 在本沙箱不可用，用户确认进程沙箱为默认真实路径
- **A3 Tool / Code 工作流节点**：新增 `ToolStepExecutor` / `CodeStepExecutor`，注册为 `StepType.Tool=6` / `Code=7` 节点执行器，经既有 `ResolveExecutor`（`HandlesType` 匹配）真实路由；前端 DAG 画布补 Tool / Code 节点（调色板 / 图标 / 配置面板 / node-type 映射）
- **配置**：新增 `SandboxSettings`（`Application.Abstractions`）+ `appsettings.json` 的 `Sandbox` 节（`Provider` 默认 `Process`、`TimeoutSeconds`、`HttpTimeoutSeconds`、`AllowedLanguages` 白名单、`NetworkEnabled` 默认 `false`、`MaxOutputBytes`、`InterpreterPaths`）；`DI` 条件注册 `ICodeSandbox`（Docker / Process）+ `AddHttpClient()`

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f5-action-layer`
- 已核对 A1 / A2 / A3 **真实副作用验收**：新增 13 例单测真实走 HTTP `SendAsync` + 真实 python/node 子进程（print→stdout、raise→stderr、sleep(30) 超时杀、ruby 白名单拒绝）
- `dotnet test src/AgentPlatform.sln` **230 passed / 0 failed**（含 F5 新增 13 例）；`tsc --noEmit` **0 error**

**已知残留（非阻断，waiver target Phase 6）：**
- 真实 Docker 容器隔离（需 Docker.DotNet + 守护进程）；Skill / MCP 执行器占位（设计为 A1 仅要求 NativeToolExecutor 真实化）
- 进程模式无法在 OS 层强制禁网，以 `NetworkEnabled=false` + 语言白名单 + 超时杀 + 输出截断缓解
- 含 Tool/Code 节点的全链路 e2e 需后端 + Web 实例，本沙箱未跑（单元层已覆盖真实执行路径）

**分支：** `feat/f5-action-layer`

## v2.3 (2026-07-24)

### F4 · 前端工程化完成（feature-builder 全栈实跑）

补齐前端工程化短板：拆包、去静态 message、清死代码、补 a11y、补单测。

**核心改动：**
- **O6 路由级拆包**：`App.tsx` 全部页面改 `React.lazy` + `<Suspense>`；`vite.config.ts` 的 `manualChunks` 函数式拆 `react-vendor` / `antd` / `xyflow` 三块供应商分包。首屏主包由 ~1.38MB 降至 `index` 9KB，供应商与页面按需并行加载（build 产物已验证）
- **O9 静态 `message` → `App.useApp()`**：LoginPage / WorkflowCanvasPage / ApiKeysPage / ConversationDetailPage / ConversationsPage / KnowledgeBaseDetailPage / KnowledgeBasesPage 共 7 个页面（WorkflowsPage/AppLayout 已于 F3 完成），消除 antd 静态 message 的 context 丢失告警；grep 全仓 0 处静态 `message.`
- **O10 死代码清理**：`appStore` 移除从未被读取的死字段 `userRole`（接口 + 5 处赋值）；编辑器节点编辑/删除（NodeConfigPanel + 删除按钮 + Delete 键）经核实已满足，不重复实现
- **O14 可访问性**：侧栏折叠按钮、会话搜索框、聊天输入框补 `aria-label`
- **O7 关键页单测**：新增 `appStore` 鉴权态迁移（5 例）、`useApiState` 加载/错误/retry/卸载安全（4 例）、`LoginPage`（3 例）、`NotFoundPage`（1 例），覆盖鉴权态 / 异步错误态 / 登录 / 404

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f4-frontend-engineering`
- **前端四道闸门 PASS**：`node scripts/qa.mjs`（typecheck / lint / build / unit）全绿
- e2e 闸门因沙箱无后端实例未执行，留待有后端环境补跑 `node scripts/qa.mjs --e2e`

**分支：** `feat/f4-frontend-engineering`

## v2.2 (2026-07-24)

### F3 · 页面交互打磨完成（feature-builder 全栈实跑）

列表/筛选/表单交互打磨 + 后端 `/conversations` 服务端筛选补完。

**核心修复：**
- **B10 状态色块错乱（根因）**：后端 `Program.cs` 未注册 `JsonStringEnumConverter`，枚举按**整数**序列化；原前端用小写字符串做 color map 的 key 永远 miss。新增 `src/status.ts` 单一事实源（`mapWorkflowStatus` / `WORKFLOW_STATUS_FILTER_OPTIONS` 整数枚举值 / `CONVERSATION_STATUS_META`），ExecutionLogs + Workflows 状态 Tag 与筛选下拉统一改用，色块正确且不再裸传小写字面量
- **B9** AgentConfigurations「View」按钮打开 Drawer 展示 `yamlContent`（等宽、可滚动，无新依赖）
- **B11** Workflows「快速运行」空名 → `message.warning` 且保持弹窗；`runWorkflow` 包 try/catch → 失败 `message.error`，成功才关弹窗并刷新
- **Conversations** 新增搜索框（ID/Agent/工作流/知识库）+ 状态筛选；由**客户端**改为**服务端**——后端 `GetConversationsQuery` 补 `status`+`q`，`ConversationsController` 绑定 `[FromQuery]`，前端 `getConversations` 改对象参数传 `status`/`q`
- **O12** ExecutionLogs / Workflows / AgentConfigurations 接入服务端分页（`total` + `onChange` → `skip/take`），与后端 `totalCount` 一致
- **O13** 四个列表 getter 支持 `AbortSignal`，各页 `useEffect` 内 `AbortController` 卸载时 `abort()`，杜绝 setState-after-unmount

**顺带修复的预存路由 bug（阻塞 e2e / 页面可用性）：**
- `AgentConfigurations` / `ExecutionLogs` / `AgentRoles` 三 controller 原用 `[Route("api/v1/[controller]")]`，ASP.NET 把类名展开为**无连字符**（`agentconfigurations` / `executionlogs` / `agentroles`），而前端一贯用连字符路径（`agent-configurations` 等）→ 404。改为显式连字符路由并同步修正 `EndpointContractTests` 断言。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f3-page-polish`
- **e2e 闸门 PASS**：`npx playwright test` **14 passed / 0 failed**（前端 cookie 鉴权规格 + 新增 `e2e/page-polish.spec.ts`）
- **后端单测 PASS**：`dotnet test src/AgentPlatform.sln` **214 passed / 0 failed**

**分支：** `feat/f3-page-polish`

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
