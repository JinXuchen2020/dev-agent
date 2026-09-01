# 变更日志

## v2.35 (2026-09-01)

### F36 · Agent 上下文隔离（Blackboard 分区 + 独立对话历史）—— 分支 `feat/f36-agent-context-isolation`（基于 f35 分支）

**后端**：
- Blackboard 软分区（决策 D1=A）：`agent:{agentId}:` 键约定 + `GetPartitionView(agentId)`（全局区+自分区、自分区键剥离前缀）+ `GetGlobalView()`（未绑定 agent 的 LLM 步骤剔除 agent 分区键，对存量数据零变化）；底层扁平存储与 F30 检查点/F25 调试器/RunningExecution 快照三个持久化格式零变更。
- `Conversation.AgentId`（D2=A）：迁移 `AddConversationAgentId`（nullable 列 + 唯一过滤索引 `IX_Conversations_TenantId_WorkflowId_AgentId` 防并发重复创建 + 复合索引覆盖查询）；`AgentCallStepExecutor` 自动创建/复用 per-agent per-workflow 会话并写入 prompt 摘要与回复消息；best-effort——持久化失败先 `Detach` 隔离再吞（防 Added 实体滞留 change tracker 毒化编排器后续 SaveChanges），OCE 穿透。
- agent 回复显式回写全局键 `agent:{agentId}:output`（D4=A），下游步骤经 `Blackboard.Get` 引用。
- 会话列表端点 `GET /conversations?agentId=` 过滤（D3=A）；种子 agent 会话 + BDD 场景（Conversation.feature，确定性无 LLM）。

**前端**：ConversationsPage agent 筛选 Select（getAgents 补 AbortSignal）+ 卡片紫色 agent 标签（agentId→名称映射）+ 新建兜底刷新携带筛选条件 + i18n 中英对称。

**决策（features/f36-agent-context-isolation.md §5，2026-08-31 用户锁定）**：D1=A 软分区 / D2=A 自动建会话 / D3=A 筛选+标签 / D4=A 显式回写。现实修正：Blackboard 实为 `Dictionary<string,string>`（非 backlog 原文的 `<string,object>`）、AgentCallStepExecutor 原本从不接触 Conversation。

**质量门**：三道门全 PASS（`.quality-gate.json` 推进 `f36-agent-context-isolation`，`cleared:true`）。ddd-code-reviewer 修复 P1（唯一过滤索引防并发双建会话）+ 3×P2（OCE 不吞用例锁定、getAgents AbortSignal、兜底刷新带筛选）；结构门 P0-P2=0（2×P3 waiver：分区预留 API、截断字面量）；optimizer Round F36-01 修 P1（Detach 隔离）+ 3×P3（doc 注释归位/if 合并/冗余单列索引移除），0 open。测试：build 0/0；Application **253** / Infrastructure **162+6skip** / Api 35 / Architecture 9 / Integration 5 / SpecFlow **115/116**（唯一失败=master 既有 LLM 用例）；新增 Blackboard 分区 7 例 + executor 7 例 + EF 会话隔离 4 例 + SpecFlow 1 场景；前端 tsc 0 error + vitest（既有豁免×2）+ vite build。质量报告 `docs/quality/f36-agent-context-isolation-gate.md`。

**已知残留（非阻断）**：硬分区（`Dictionary<Guid,…>` 重构 + 持久化 SchemaVersion 升级）列 v2；分区写入 API（SetInPartition/GetFromPartition）v1 为预留、agent 工具链落地时接入；截断字面量 8000/12000 未抽配置。

## v2.34 (2026-08-31)

### F35 · 多工作空间隔离（Workspace）—— 同租户内第二层隔离维度（分支 `feat/f35-workspace-isolation`）

**后端**：
- Domain：`Workspace`/`WorkspaceMember` 新聚合（不实现 `IWorkspaceScoped`——其 `WorkspaceId` 是数据而非隔离范围）+ `IWorkspaceScoped` 接口 + `IWorkspaceRepository`/`IWorkspaceMemberRepository`；18 个业务聚合全量加 `WorkspaceId`（`AuditLog`/`ExecutionLog`/`AgentRunRecord` 仅补列不过滤，决策 D2=A）。
- Infrastructure：`AppDbContext` 组合 query filter（tenant AND workspace，闭包 field per-query 求值）+ `SaveChanges` 对新增 `IWorkspaceScoped` 实体自动注入当前工作空间（显式赋值优先）；`IWorkspaceProvider` 三级解析链（JWT `workspace_id` claim → `X-Workspace-Id` header → `WorkspaceDirectory` 租户默认工作空间兜底 → 空=fail-closed）；`WorkspaceProvisioner`（幂等补默认工作空间 + 空 `WorkspaceId` 存量行回填）；迁移 `20260831052610_AddWorkspaceIsolation`（2 新表 + 21 表加列 + 唯一索引）。
- Api：`WorkspacesController`（list/create/update/delete/members CRUD/switch，switch 重签 JWT + cookie）；登录写 `workspace_id` claim、`/auth/me` 返回 `currentWorkspaceId`、dev-login 支持可选 `workspaceId`；`WorkspaceHeaderGuardMiddleware`（非 Admin 剥离不可见的 `X-Workspace-Id` 头，Admin 亦校验头 id 属于本租户）；API-Key 认证成功后把请求 scope 钉到 Key 所属工作空间（F22 发布端点/MCP 不受影响）。
- 触发路径：`GetByIdForTriggerAsync`（仅按租户定位）修复非默认工作空间工作流被触发器静默跳过的回归；调度执行 v1 语义 = 落租户默认工作空间（设计文档已知限制）。

**前端**：
- `WorkspaceSwitcher`（顶栏：Select 切换 + Admin 管理菜单 = 新建/编辑/成员管理 Drawer/删除守卫提示）+ `api.ts` 请求拦截器注入 `X-Workspace-Id` + `appStore.currentWorkspaceId`（localStorage `app-workspace-id` 持久化）+ `useApiState` 订阅 workspace 变更全站自动刷新（决策 D5=A，单点改 hook）+ i18n 中英对称。

**决策（features/f35-workspace-isolation.md §6，2026-08-31 用户锁定）**：D1=C claim+header 双通道 / D2=A 18 聚合 / D3=B 成员表 / D4=删除守卫绝不级联 / D5=A 状态驱动刷新。

**质量门**：三道门全 PASS（`.quality-gate.json` 推进 `f35-workspace-isolation`，`cleared:true`）；ddd-code-reviewer 修复 2×P1（header 越权剥离中间件、触发路径回归）+ 3 项 P2/P3；ddd-phase-quality-gate P0-P2=0（1×P3 已修 + 2×P3 waiver）；codebase-optimizer Round F35-01 0 open（1×P3 存储键常量单源化 + 5×P3 waiver）。测试：后端 build 0/0 + Application 238 / Infrastructure 158+6skip / Api 35 / Architecture 9 / SpecFlow 114/115（唯一失败为 master 既有 LLM 用例）/ Integration 5（需 `OPENAI__Key` 环境变量）；新增 Application handler 测试 12 例 + Infrastructure EF 隔离测试 4 例；前端 tsc 0 error + vitest（2 个 master 既有失败豁免）+ vite build 通过；BDD E2E `e2e/features/workspace-switch.feature`（CI 驱动）。质量报告 `docs/quality/f35-workspace-isolation-gate.md`。

**已知残留（非阻断）**：触发/调度执行仅落租户默认工作空间；成员列表 N+1（量小可接受）；workspace 名称唯一性大小写语义依赖 DB collation；AuditLog/ExecutionLog/AgentRunRecord 运行期 WorkspaceId 恒空（D2=A 设计，仅为未来过滤预留）。

## v2.33 (2026-08-28)

### CI/E2E 真实 Key 链路修复系列（8 commits，`496f3bb` → `05028e6`）——「E2E 用真实 key 不用 stub」方向全面收口

F41 落地后，集成测试与前端 E2E 全部切到真实 LLM，暴露一批环境映射与测试隔离问题，本轮集中根治：

**CI 环境变量映射（`496f3bb`/`c9157e3`/`a6396b8`）**：
- CI 注入 `OPENAI_API_KEY`（单下划线），.NET 配置绑定读 `OpenAI:Key` 需**双下划线**环境变量覆盖——`scripts/integration.mjs` 在 `startBackend` 把 `OPENAI_API_KEY/OPENAI_BASE_URL/OPENAI_MODEL` 映射为 `OpenAI__Key` 等注入 frontend-e2e 后端；此前 `Program.cs:93` 启动守卫读不到 Key 直接抛 `InvalidOperationException` 崩溃，`/health` 永远起不来。

**SpecFlow 超时（`c5042f5`）**：
- `IntegrationAppFactory` 单例 `Api`（CreateClient 默认 `Timeout=100s`）在真实 LLM 冷启动/抖动下被截断为 `TaskCanceledException`——放宽至 `TimeSpan.FromMinutes(5)`，F12 宿主一并受益。

**测试隔离与断言加固（`7f35864`/`23ed7b5`/`714142f`/`05028e6`）**：
- `/debug/step` 500 真因：credentials E2E 场景创建的 BYO 凭据（假 key + gpt-4o + 空 BaseUrl→api.openai.com）**污染默认租户**，ModelRouter「BYO 优先」使后续所有真实 LLM 调用走必失败凭据 → 修复为测试自清理（场景末尾 DELETE 凭据，接受 200/204）。前两轮「列截断」迁移（7f35864/23ed7b5）基于 SQLite 不强制 varchar 长度的实证被推翻，无害保留。
- agentic-run.feature「最终回答」断言：真实 key 下 `AgenticOrchestrator` 无工具调用分支连发两次模型请求（探测 + 流式）常超 20s；且模型 429/异常时 `runError` 置位致「最终回答」区块永不渲染、原断言静默超时掩盖真实失败——改为等终态（`最终回答` OR `.ant-alert-error`）90s，错误先现抛真实原因。

**质量门**：每笔 src/ 改动均带 `.quality-gate.json`（聚焦修复口径）+ `Quality-Gate:` 行，报告见 `docs/quality/phase-6-frontend-e2e-500-gate.md` 与 `phase-6-frontend-e2e-agentic-run-gate.md`。本地前端 E2E 27/27。

## v2.32 (2026-08-26 ~ 08-27)

### F41 · 移除 QuickStart 模式、强制真实 Key、平台模型配置 DB 化（commit `a11a6c6` + `62ede44`，BREAKING）

**BREAKING CHANGE**：不再提供 QuickStart（Stub 模型）零依赖一键体验；`Development`/`Production`/`Staging` 启动强制校验至少一个真实 LLM Provider（`OpenAI:Key` 或 `OpenAI:BaseUrl`），无 Key fail-fast 抛 `InvalidOperationException`。`ModelClient:Provider=Stub` 仅 `Test` 环境生效。设计文档 `features/f41-remove-quickstart-enforce-real-keys.md`。

- 删除 `launchSettings.json` QuickStart profile 与相关环境判断；删除 `StubTenantModelClientResolver`
- CI workflow 合并去重（`c3c4b89`），保留综合 `ci.yml`
- **平台模型配置 DB 化**（`62ede44`）：移除 `RouterSettings.Candidates` 静态配置，平台模型由 DB-backed `PlatformModels` 驱动——平台模型增删改从「改 appsettings + 重启」变为「后台管理即时生效」，与租户 BYO 凭据同构

## v2.31 (2026-08-25)

### F34 · 在线评估门禁完成（feature-builder 全栈闭环，🟢低风险，三道质量门全 PASS）——二期 F29–F34 全部收口

F34 v1 将 F24 离线评估升级为**带阻断语义的部署门禁**：CI/发布流水线调用门禁端点，通过率未达阈值返回 HTTP 422（body 含完整报告），达成则 200——「真实阻断」而非仅报告。执行复用 RunEvaluation 一次性克隆路径，影子隔离零生产写入。

**核心改动：**
- **RunEvaluationGateCommand/Handler**：阈值解析链（请求显式 > `EvaluationSettings.GateMinPassRate`=0.8）；越界阈值抛 ArgumentOutOfRange；**空数据集显式守卫恒不通过**（防「无数据即放行」）
- **端点**：`POST /api/v1/evaluation-datasets/{datasetId}/gate/{workflowId}`（Admin/Operator），remarks 含 CI curl 阻断用法示例
- **审计归因**：新增 `AuditActionType.EvaluationGate`（Aggregates 生效枚举，字符串存储无迁移），details 记录 score vs threshold 与 PASS/BLOCK

**测试**：新增 `RunEvaluationGateCommandHandlerTests` 5 例（超阈值通过+审计断言、低于阈值阻断、显式覆盖配置、空数据集零阈值仍拦、越界抛错）。全绿 App226 / Infra154+6skip / Api35 / Arch9；build 0/0。前端零改动。设计文档 `features/f34-online-eval-gate.md` §5 含二期收口说明。

**延后项（独立排期）**：CI YAML 接入样例、队列化执行/水平扩展、监控告警聚合、异常回放诊断入口。

## v2.30 (2026-08-25)

### F33 · 语义记忆层完成（feature-builder 全栈闭环，🟡中风险，三道质量门全 PASS）

F33 把平台从「文件注入式记忆」升级为语义记忆引擎：跨运行经验沉淀 + 语义召回注入，并打通 Summary/Retrieval 到 LLM prompt 的「最后一公里」（修复上下文通道建而不用的隐性漂移）。

**核心改动：**
- **① Embedding 管线**：`ISemanticMemoryService` 复用 IVectorStore（租户隔离、Pg/InMemory 双实现），集合 `semantic-memory`；内容寻址 docId 同内容去重
- **② Episodic 写回**：WorkflowCompleted / WorkflowRolledBack 双事件 handler——成功经验与失败教训（含 errorDetail）均沉淀；Enabled 开关；异常仅告警不影响主流程
- **③ 自动 Compaction**：BuildWorkflowContext 溢出步骤由硬截断丢弃改为按当前节点语义召回 Top-K 经验（负数键 `[semantic-recall]` 注入 Summary）；服务缺席优雅退回现状
- **Prompt 打通**：AgentCallStepExecutor 新增 History summary / Relevant knowledge 区块——Summary（含召回）与 Retrieval.Chunks 首次真正进入模型输入

**测试**：新增 7 例（服务写穿/确定性 id/召回透传 3 · 写回 handler completed/rolled_back/disabled 3 · prompt 渲染 1）。全绿 App221 / Infra154+6skip / Api35 / Arch9；build 0/0。前端零改动。设计文档 `features/f33-semantic-memory.md` §6。

## v2.29 (2026-08-25)

### F32 · Agent 消息总线 + 多 Agent 协作完成（feature-builder 全栈闭环，🟡中风险，三道质量门全 PASS）

F32 为平台引入「agent 社会原语」：进程内消息总线（Channel<T> 有界背压、写穿持久化、幂等消费）+ Negotiation 预设升级为**真并行多 agent 协作**——绑定 agent 的步骤经 Task.WhenAll 并发提案（时间窗重叠实证），critic 拒绝自动 Critique+Handoff 定向移交并携带反馈上下文，预算/停滞/环路指纹三防线熔断。

**核心改动：**
- **① 总线**：`IAgentMessageBus` + `InProcessAgentMessageBus`（每 receiver 有界 Channel 256；SCOPED=运行级隔离）；`AgentMessage` 契约（CorrelationId/Round/Type/Payload）
- **② 并行协作**：NegotiationOrchestrator 双模式——协作门禁（绑定 agent + 基础设施齐备）→ 并行提案相位（纯网络 I/O，EF 触碰严格留在线程外）；无绑定 agent 诚实降级既有串行循环
- **③ 持久化+幂等**：`AgentMessageLog` 聚合（ITenantScoped，迁移 AddAgentMessageLog）；TryMarkConsumed 条件更新幂等门；RepublishUnconsumed 跨轮重投
- **④ 防治+可观测**：单轮预算 64 / 停滞 120s / 环路指纹 ≥3 三防线熔断 Paused+告警日志；CorrelationId 全链 trace 回放
- **附带修复**：`nvarchar(max)` 列类型在 SQLite EnsureCreated/MigrateAsync 的 DDL 语法错误（曾致 Api.Tests 31 例连锁失败）——统一改 `text` 并回改 F30 迁移，跨三大数据库提供商安全

**测试**：新增 7 例（总线持久化/去重/隔离/重投 4 + 双 agent 并行重叠/handoff 定向/预算熔断 3）。全绿 App217 / Infra151+6skip / Api35 / Arch9；build 0/0。前端零改动。设计文档 `features/f32-agent-message-bus.md` §8。

## v2.28 (2026-08-25)

### F31 · Agent 运行时实体化 + 模型接通完成（feature-builder 全栈闭环，🔴高风险，三道质量门全 PASS）

F31 消除蓝图标记的最高优先缺陷「配而不生效」：给节点绑定的智能体在执行时真实生效——SystemPrompt 驱动 prompt、模型经 ModelRouter 按「租户 BYO 优先 → 平台回退 → 候选降级」解析。**用户从此只需在「我的凭据」加一条 BYO 凭据，工作流节点即可真实调用 LLM**（此前该路径对 BYO 完全无效）。

**核心改动：**
- **AgentCallStepExecutor 实体化**：按 `AssignedAgentId` 加载聚合（租户过滤器防跨租户）；绑定 agent → 真实 SystemPrompt + `PreferredModel=agent.ModelEndpoint.ModelName`；未绑定 → 向后兼容通用模板；agent 缺失 → fail-loud 明确报错
- **CriticStepExecutor 接通 Router**：不再硬编码 DefaultModelId 直连平台客户端；AllowCriticOverride fail-loud/open 语义保持
- **ModelRouter 空候选守卫**：新增 `ModelNotConfiguredException`（指明「我的凭据 / 平台 Key」两条配置路径），替代笼统 AllModelsFailedException

**附带修复三项（实现过程中实证暴露）：**
1. **F30 回归**：陈旧 RunningExecution 租约阻断重跑/恢复——`TryAcquireLease` 移除「仅 Running 可租」门禁；WorkflowTriggersIntegrationTests 2 例转绿实证
2. **多实例租约守卫失效 bug**：TryAcquireLease 属性自比恒 true → 任意实例可抢活跃租约；改为参数 vs 持有者正确比较 + 新增 `Rehydrate` 工厂
3. **生产缺陷**：`ResolveBashPath` 兜底命中 System32 WSL 桩——无 Git Bash 的 Windows 上所有 run_command 必败且报乱码；排除系统目录桩 + echo 实测探针

**测试**：新增 19 例（AgentCall 5 / Critic 4 / RouterNotConfigured 2 / RunningExecution 8）。全绿 App 214 / Infra 147+6skip / Api 35 / Arch 9；build 0/0。前端零改动。质量报告见 `features/f31-agent-runtime.md` §8。

## v2.27 (2026-08-24)

### F30 · 执行持久化完成（feature-builder 全栈闭环，🔴高风险，三道质量门全 PASS）

F30 将「请求同步跑完」的编排器升级为 **可挂起 / 可恢复 / 崩溃可重启** 的持久执行引擎：每步落检查点 → 进程崩溃后从最近检查点续跑，**不重跑**已完成步；DB-backed in-flight 真相源替代静态 `ConcurrentDictionary`；`WorkflowScheduler` 升级为 durable 驱动器（租约/心跳/过期扫描，多实例幂等）。

**核心改动（后端）：**
- **① 检查点模型**：`ExecutionLog` 新增 `CheckpointData` (JSON) + `CheckpointVersion` (乐观并发) + 迁移 `20260824013403_AddDurableExecutionCheckpoint`（含 `#pragma warning disable IDE0161`）。
- **② RunningExecution 聚合**：新增 `RunningExecution`（主键=WorkflowId、租户隔离、租约/心跳/检查点版本/Blackboard 快照）+ `IRunningExecutionRepository` + `RunningExecutionRepository` + 迁移 `20260824014109_AddRunningExecution`（`ValueGeneratedNever()`、`HasQueryFilter`）。
- **③ 编排器耐久化**：`OrchestrationPrimitive` 重写——`RunAsync` 获取租约、每步落检查点、`PauseAsync`/`ResumeAsync`/`ResumeFromCheckpointAsync`（内部）更新 `RunningExecution`；静态 `s_runningCts` 字典废弃。`SequentialOrchestrator` 支持 `resumeFromCheckpoint`，从 `ExecutionLog.CheckpointData` 反序列化恢复 `Blackboard`/节点状态/`skipSet`/执行索引，**跳过已 Completed 节点**；检查点批处理（可配 `DurableExecutionSettings.CheckpointBatchSize=5`、`CheckpointMaxAgeSeconds=30`），终态强制 flush。
- **④ 调度器耐久化**：`WorkflowScheduler` 扫描 `RunningExecution` 租约过期记录，抢占租约后调用 `OrchestrationPrimitive.ResumeFromCheckpointAsync`，多实例幂等（仅一实例成功 `TryAcquireLease`）。
- **⑤ 可配置化**：新增 `DurableExecutionSettings`（`LeaseTtlMinutes`、`CheckpointBatchSize`、`CheckpointMaxAgeSeconds`），`appsettings.json` 可配，DI 绑定 `IOptions<DurableExecutionSettings>`。

**测试**：`OrchestrationPrimitiveTests` 全绿（23/23），覆盖 Run/Resume/Pause/Retry/Rollback/GetState/Debug/条件分支/循环/崩溃恢复语义。全量 `dotnet test` 0 失败。

**质量门**：`dotnet build` 0/0，前端 `npm run build` (tsc + vite) 通过，三道质量门全 PASS（`.quality-gate.json` 推进 `f30-durable-execution`，`cleared:true`）。质量报告 `docs/quality/f30-durable-execution-gate.md`，设计文档 `features/f30-durable-execution.md`。

**文档同步**：`features/backlog.md` F30 标记 `done`，`AGENT_PLATFORM_BLUEPRINT.md` §Phase 7 更新实现状态，`appendices/core-aggregates.md` 新增 `RunningExecution`。

## v2.26 (2026-08-21)

### F29 · Agentic Agent Primitive（自主 Agent 控制循环原语）完成（feature-builder 全栈闭环，🔴高风险范式跨越，三道质量门全 PASS）

F29 把「Agent 配置实体」变成「真自主 Agent」：模型工具调用通道 + ReAct 控制循环 + 工作区工具 + 安全护栏，端到端落地（后端 + 前端 + BDD E2E）。`features/agentic-agent-primitive.md` §12 含完整质量门清单与 Review-Fix 记录。

**核心改动（后端）：**
- **① 模型工具调用通道**（最大 blocker 已解）：`IModelClient.ChatAsync` 增 `IReadOnlyList<ToolDefinition>? tools` 参数；`ModelResponse`/`ChatMessage` 增 `ToolCalls` 字段；`SemanticKernelModelClient` 按 **SK 1.30 真实 API** 接线——`OpenAIPromptExecutionSettings.ToolCallBehavior = ToolCallBehavior.EnableFunctions(fn, autoInvoke:false)` declare-only、工具经 `KernelFunctionMetadata.ToOpenAIFunction()` 构建（`OpenAIFunction` ctor 为 internal）、助手 tool_calls 用 `FunctionCallContent` 回显、tool 结果用 `FunctionResultContent` 配对（两类型在 `Microsoft.SemanticKernel` 命名空间）。此前误写的 `ToolCallContent`/`NoKernelFunctions`/`OpenAIChatPromptExecutionSettings`/3 参 `OpenAIFunction` 均不存在，经反射核实后全部改正。
- **② ReAct 控制循环**：新 `AgenticOrchestrator`（plan→act→observe→reflect，工具调用经 `ToolCallingDispatcher` 分发，结果回灌；无 tool call 判停；硬迭代上限抛 `AgentIterationLimitExceededException`；白名单护栏拦截非允许工具并记录 `tool_not_allowed`）。`StepType.Agentic=15` + `AgenticStepExecutor` 使自主 agent 成为 DAG 节点（混合编排，`HandlesType` 显式路由）。
- **③ Agent 字段 + 迁移 + 种子**：`Agent` 聚合增 `AllowedToolNamesJson`/`MaxIterations`/`StopCriteria`（EF 映射 + 迁移 `AddAgentAgenticFields`，含 IDE0161 pragma）；`DatabaseInitializer` 幂等种子 F29 demo agent（固定 Guid `3333…3301`，工作区工具白名单）。DTO/命令/API 全链路打通，新增 `POST /api/v1/agents/{id}/runs`（Admin,Operator，未找到 agent 返回 404）。
- **④ Workspace 工具**：`WorkspaceToolExecutor`（read/write/edit/list_files/run_command/git_diff）在真实沙箱内执行，`ICodeSandbox.RunCommandAsync` 增 `workingDirectory`（修复命令跑在宿主 CWD 缺陷）；修 UTF-8 BOM 写文件缺陷；路径逃逸 + 命令黑名单护栏。
- **⑤ 测试**：3 个新测试类 13 用例（`AgenticOrchestratorTests` 4 / `WorkspaceToolExecutorTests` 6 / `SemanticKernelModelClientToolCallTests` 3），覆盖验收 ①②④⑤。全量 `dotnet test` 0 失败（App 192 / Infra 147+6skip / Api 35 / Arch 9 / SpecFlow BDD 115）。

**核心改动（前端）：**
- Agent 表单新增「允许工具（多选）」「最大迭代」「停止条件」；卡片显示工具数/迭代标签；新增运行弹窗（输入目标 → `POST /agents/{id}/runs` → 展示最终回答 + 迭代/令牌 + 逐步 trace）。类型/API/i18n（zh/en）对称补齐。
- **BDD E2E**：`agentic-run.feature`（1 场景：建带工具白名单 agent → 运行 → 显示最终回答）全链路通过；全量 `@e2e` 26/26 通过（含对重名 agent 的 `.first()` 定位稳健化）。

**模型一致性**：Agent 三字段类型/枚举（`StepType` int）/DTO 全对齐；`tsc --noEmit` 0、eslint 0 error、`dotnet build` 0 警告 0 错误。质量报告 `docs/quality/f29-agentic-gate.md`，`.quality-gate.json` cleared:true。

## v2.25 (2026-08-11)

### F8 · 差异化优势产品化（Negotiation + Critic）前端专属模式完成（feature-builder 纯前端闭环，🟢低风险）

F8 将后端已就绪的 Negotiation 协商式多智能体 + Critic 收敛原语**产品化为画布专属模式**。后端零改动（OrchestrationPreset.Negotiation / NegotiationOrchestrator / CriticStepExecutor / DetectPreset 原语齐全），纯前端实现。三道质量门全 PASS。

**核心改动：**
- **编排模式选择器**：`WorkflowCanvasPage` 工具栏新增 antd `Segmented`（自动/顺序/协商），`auto` 省略 preset 由后端 `DetectPreset` 自动识别；`sequential`→int 0；`negotiation`→int 1。模型一致性关键：API 全局未注册 `JsonStringEnumConverter`，preset 一律以 **int 收发**，绝不可改字符串。
- **协商模式可见指示**：当 `presetMode==='negotiation'` 或画布含 `StepType.Critic` 节点时，显示紫色 `Tag`（协商模式 · 评审收敛），让 Critic 收敛特性在 UI 上可感知。
- **一键脚手架**：`workflowCanvasStore.scaffoldAgentTeam()` 单次 history 快照生成 `Start → Architect → Developer → Critic → End` 五节点四边协商图（严格满足 `ValidateGraph`：单 Start、≥1 End、无环、全连通、节点名唯一）。
- **模型一致性**：`runExistingWorkflow(id, mode)` 映射 `OrchestrationPresetMode` → 后端 preset int；`services/api.ts` 与 `types/index.ts` 新增 `OrchestrationPresetMode` 类型（'auto' | 'sequential' | 'negotiation'）。
- **i18n**：`zh-CN.ts` / `en-US.ts` 对称补充 `preset` / `negotiationMode` / `scaffoldAgentTeam` 三键。
- **BDD E2E**：新增 `agent-team-negotiation.feature`（1 场景）+ `agentTeam.steps.ts`，采用「新建工作流 + 两次保存并运行」路径——首次线性创建跳转编辑页（生成 id），第二次在既有工作流上走 `runExistingWorkflow(id,'negotiation')` 真实 DAG 协商运行并断言 `Completed` 终态；后端不可达时整体 skip。`bddgen` + `playwright test --list` 0 未定义步骤。
- **质量门**：`tsc --noEmit` 0、`vite build` 0、`eslint`（改动文件）0 error、三道门 0 open。质量报告 `docs/quality/f8-negotiation-gate.md`，设计文档 `features/negotiation-productization.md`。

## v2.24 (2026-08-11)

### F12 · Tool/Code 节点全链路 e2e 完成（feature-builder 全栈实跑，🟢低风险闭环）

F12 起**真实后端 + 真实 Tool/Code 执行器**，跑一条含 `StepType.Tool`（真实 HTTP）与 `StepType.Code`（真实 python 子进程）节点的工作流，断言端到端 stdout/响应回填与节点状态。三道质量门全 PASS。顺带**暴露并修复一个真实平台缺陷**。

**核心改动：**
- **测试基础设施解封**：`IntegrationAppFactory` 抽 3 虚钩子（`DbPath`/`StripStepExecutors`/`IntegrationConfiguration`），基默认行为不变；新增 `RealStepsIntegrationAppFactory`（`StripStepExecutors=false` 保留真实执行器、`Sandbox:Provider=Process`、独立 DB `test-integration-f12.db`、`DetectPythonCommand` 解析 `python`/`python3`）。
- **本地回环 Tool echo 端点**：新增 `ToolEchoServer`（`TcpListener` 回环动态端口最小 HTTP 响应器，规避 Windows `HttpListener` URL ACL），测试向 `IToolRegistry` 注册 `bdd-echo-tool` 指向其 `BaseUrl`。
- **BDD 场景**：新增 `WorkflowCodeToolE2E.feature`（1 场景）+ `F12IntegrationHost`/`F12IntegrationClient`/`WorkflowCodeToolE2ESteps`，复用既有 harness；实测 Code 节点 `Result="hello-from-code\r\n"`（真实 python stdout）、Tool 节点 `Result='{"echo":"ok","tool":"bdd-echo-tool"}'`（真实 HTTP 响应）、二者 `State=Completed`、`execution-logs` 回填同含真实输出。
- **关联平台修复（真实缺陷）**：`Workflow._isDag` 未做 EF 持久化，致 DAG 工作流**重跑**时 `IsDag` 复位 `false`、`SequentialOrchestrator.PrepareContext` 静默 fallback 到遗留 `Steps` 投影，所有 `Code`/`Tool` 节点不执行而工作流整体 `Completed`（典型"假完成"）。→ `WorkflowConfiguration` 映射 `IsDag` 列（not null 默认 false）+ 新增迁移 `PersistWorkflowIsDag`（含 `#pragma warning disable IDE0161` 以符合 `TreatWarningsAsErrors` 铁律）。此为通用 DAG 重跑缺陷，对所有含节点工作流的 run 接口生效。
- **测试**：新增 F12 场景 1 项；全量 `dotnet test` 0 失败（6 程序集：Arch9/App188/Infra138+6skip/Integration5/Api35/SpecFlow115，既有 114 BDD 未被 `IsDag` 修复破坏）。`dotnet build` 0 警告 0 错误。
- **质量门**：`ddd-code-reviewer` 发现并即时修复 **IDE0161 编译阻断** + **控制标记断言误判** + **IsDag 平台缺陷** 共 3 项；`ddd-phase-quality-gate` 12 类审计全过；`codebase-optimizer` 七维 0 阻断（分析模式，不建分支/不 push）。质量报告 `docs/quality/f12-tool-code-e2e-gate.md`。设计文档 `features/tool-code-e2e.md` §8 记录关联平台修复。

## v2.23 (2026-08-07)

### F34 · 沙箱双层隔离（Docker 默认强隔离 + JobObject/AppContainer 兜底）完成（feature-builder 全栈实跑，⚠️中风险闭环）

F34 收敛 `docs/sandbox-isolation-harness-comparison.md` §7 的差距建议：默认走 Docker 容器强隔离，无守护进程时降级 F11 的 JobObject/AppContainer 进程级兜底，并显式告知用户隔离强度。三道质量门全 PASS。

**核心改动：**
- **唯一入口收敛**：`ICodeSandbox` 仅注册 `ProcessCodeSandbox`（移除原 `Provider=Docker → DockerCodeSandbox` 并列注册）；`DockerCodeSandbox` 降级为内部容器执行器，由 `DockerSandboxIsolation` 持有复用。
- **强隔离接入**：新增 `DockerSandboxIsolation : ISandboxIsolation`（注入 `IDockerProbe` + `DockerCodeSandbox`），`CanLaunch ⇒ Docker 守护进程可用`，`Strength ⇒ Strong`；`TryLaunchAsync` 委托 `DockerCodeSandbox.RunCodeAsync`（NetworkMode=none + 内存限额 + 只读代码挂载）并以 `result with { IsolationStrength = Strong }` 标注结果，异常/不可用返回 `null` 透明回退。
- **守护进程探测**：新增 `IDockerProbe` 单例，构造时一次 `DockerClientConfiguration().CreateClient().PingAsync()`（2s 超时 + 全 `try/catch`），`IsAvailable` 缓存；不可用记告警、不抛、不阻塞启动（fail-safe）。
- **强度标注**：`Application.Abstractions` 新增 `IsolationStrength` 枚举（None/Weak/Strong）；`SandboxResult` record 末尾追加 `IsolationStrength IsolationStrength = IsolationStrength.Weak`（带默认值，向后兼容）；`ISandboxIsolation` 扩展 `Strength` 属性，F11 三实现分别返回 Weak/Weak/None；`ProcessCodeSandbox` 构造 `SandboxResult` 时填入 `_isolation.Strength`。
- **DI 工厂**：`ISandboxIsolation` 按 `Provider + Docker 可用 + 平台 + OsIsolation` 解析——`Provider=Docker` 且守护进程可用 → `DockerSandboxIsolation`；否则 Windows+AppContainer/Full → `AppContainerSandboxIsolation`；Windows+JobObject/默认 → `JobObjectSandboxIsolation`；非 Windows/Off → `NullSandboxIsolation`。
- **配置**：`appsettings.json` `Sandbox.Provider` 默认 `"Docker"`（强隔离优先，守护进程不可用自动降级 fail-safe）；其余 Sandbox 字段不变。
- **范围边界**：`ProcessCodeSandbox.RunCommandAsync`（shell 命令）保持 F11 行为（不经 Docker）；不引入 gVisor/Firecracker/新 NuGet 包；前端无沙箱 UI、无前端变更。
- **测试**：新增 `DualLayerSandboxTests`（7 项：探测 fail-safe / 模式切换 / `Attach=false` / Strong 结果 `SkippableFact` / 回退 Weak `SkippableFact`(Windows) / 向后兼容）；F11 兜底路径全绿。全量 `dotnet test` 0 失败（6 程序集）。
- **质量门**：`ddd-code-reviewer` 发现并即时修复 **P1×1**（`DockerSandboxIsolation` 未将结果标注为 Strong，静默违反核心契约）+ **P2×1**（设计文档 `ReadonlyRootfs` 表述漂移已校正）+ **P3×1**（测试 `Process.Start("echo")` Windows CI 失败已改 SkippableFact）；`ddd-phase-quality-gate` 12 类审计全过；`codebase-optimizer` 七维 0 阻断（分析模式，不建分支/不 push）。质量报告 `docs/quality/f34-dual-layer-sandbox-gate.md`。

## v2.22 (2026-08-07)

### F11 · 沙箱 OS 级隔离增强（JobObject 资源限额 + AppContainer 真实禁网）完成（feature-builder 全栈实跑，⚠️高风险闭环）

F11 让 `Process` 沙箱（默认 `Sandbox:Provider=Process`）获得 OS 级网络隔离与资源约束，使 `SandboxSettings.NetworkEnabled=false` 真正生效。三道质量门全 PASS。

**核心改动：**
- **隔离抽象**：新增 `ISandboxIsolation` + 三实现 `JobObjectSandboxIsolation`（Windows Job Object 资源限额：作业/进程内存上限、活动进程数上限防 fork 炸、CPU 速率硬上限）、`AppContainerSandboxIsolation`（无 `internetClient` 能力的 AppContainer profile 内启动解释器真实禁网 + 内部叠加 JobObject）、`NullSandboxIsolation`（非 Windows/Off 回退仅环境标记缓解项）。
- **接入**：`ProcessCodeSandbox` 注入 `ISandboxIsolation`；`CanLaunch` 的隔离器（AppContainer）先行启动，失败 null 回退常规 `Process.Start`；其余路径 `Attach` 事后挂接 JobObject。`ICodeSandbox`/`SandboxResult` 对外契约不变。
- **配置**：`SandboxSettings` 新增 `OsIsolation`（`Off`/`JobObject`/`AppContainer`/`Full`，**默认 `JobObject`**）+ `MaxProcessCount`(16)/`MemoryLimitBytes`(256MB)/`CpuRatePercent`(50)；`appsettings.json` Sandbox 节扩展。
- **失败安全**：任何 OS 机制不可用（权限/平台/解释器文件系统不可达/API 未导出）一律透明回退环境标记缓解项，绝不阻断代码执行。纯 `kernel32.dll` P/Invoke，无新增 NuGet 包。
- **测试**：新增 `SandboxIsolationTests`（5 项，含 Windows JobObject 实测 + AppContainer fail-safe 不变量）；全量 `dotnet test` 0 失败（6 程序集）。

## v2.20 (2026-08-06)

### F26 · 企业增强 v1（用量仪表盘 + 工作流 diff）完成（feature-builder 全栈实跑，🟢低风险闭环）

F26 按用户决策 **v1 仅做低风险纯增量（用量仪表盘 + 工作流 diff）**，多工作空间（第二租户维度）独立排期、不触碰 `ITenantScoped`/`TenantProvider`。三道质量门全 PASS。

**核心改动：**
- **用量仪表盘（后端）**：`GetWorkflowUsageQuery`（`Analytics`）按工作流聚合执行数 / 成功率 / token / 平均时延，支持 7/14/30 天范围（`AnalyticsController` 做上限校验）；端点 `GET /api/v1/analytics/workflows`。
- **工作流 diff（后端）**：`DiffWorkflowQuery`（`Versioning`）`POST /api/v1/workflows/{id}/diff`，以**稳定键**比对两版本——节点按 `Name`、边按「端点名 + label」（修复快照 id 每次保存重生导致 id-based diff 全错）；`WorkflowGraphSnapshot.FromWorkflow` 改用 `Workflow.GetEffectiveGraph()`（兼容 `_steps`-only 旧工作流空快照）。
- **用量仪表盘（前端）**：`WorkflowUsagePage`（`/usage`）KPI 卡片（执行数/成功率/token/平均时延）+ 竖向 `BarChart` 执行数 + 可排序 Table + `Segmented` 7/14/30 天切换；`App.tsx` 路由 + `AppLayout.tsx`「用量」菜单（`BarChartOutlined`）。
- **工作流 diff（前端）**：`WorkflowDiffModal`——概览标签 + 上下文变更 `Descriptions` + 新增/移除/变更节点 `Collapse` + 边新增/移除段；`WorkflowsPage` 版本抽屉「对比」按钮拉取并打开；`api.ts`/`types`/`locales`（中-en i18n 对称，`pages.usage.*` / `pages.workflows.diff.*`）。
- **BDD E2E**：`e2e/features/workflow-usage.feature` 覆盖用量页渲染 + 版本历史抽屉打开（全绿）；`bddgen` 绑定生成 `e2e/.features-gen/.../workflow-usage.feature.spec.js`。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f26-enterprise-enhancements`，`cleared:true`
- 后端 `dotnet build` **0/0**；`Application.Tests 188` / `ArchitectureTests 9` / `Api.Tests 35` / `Infrastructure.Tests 124` 全绿
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit，含 i18n 对称，vitest 44）+ `eslint 0`
- 对抗式 `ddd-code-reviewer`：修复 **P1×3**（边「changed」误报——快照 id 重生、移除 changedEdges 概念；`NodeEquals` 运算符笔误 `x.X==y.Y`→`x.X==y.X`；旧式 `_steps` 工作流空快照——`GetEffectiveGraph`）+ **P2×1**（重复节点名 `ToDictionary`→`ToNameMap` 首名优先）+ **P3×1**（删死 const `MaxRangeDays`）
- 质量报告 `docs/quality/f26-enterprise-enhancements-gate.md`
- 已知残留（非阻断）：①多工作空间（第二租户维度）不在 v1，独立排期；②`pages.workflows.diff.changedEdges` i18n key 保留未消费（未来清理）

### F9 · 代码沙箱容器隔离（DockerCodeSandbox 真实化）完成（feature-builder 全栈实跑，⚠️中风险闭环）

解决 F5 质量报告记录的 `DockerCodeSandbox` 空心类 waiver：由「显式抛异常」改为经 `Docker.DotNet` 3.125.15 **真实拉起隔离容器**执行代码 / 命令，Agent 的 Code 节点在容器化部署下具备 OS 级隔离。三道质量门全 PASS。

**核心改动（纯后端，无前端契约变更）：**
- `DockerCodeSandbox` 重写：写临时代码文件 → 只读 bind 挂载 `/sandbox/code.<ext>` → `CreateContainer` → `StartContainer` → `WaitContainer`（与 `Task.Delay(timeout)` 竞速，超时即 `KillContainer`）→ `GetContainerLogs` 捕获 stdout/ExitCode → `RemoveContainer(Force)` 清理。镜像缺失经 `Images.CreateImageAsync` 自动拉取。
- 语言映射 `python:3.12-slim` / `node:20-slim`；`RunCommandAsync` 以 `alpine:3.20` 跑 `sh -c`。`AllowedLanguages` 白名单外拒绝；`csscript` 在 Docker 模式不支持。
- 安全边界：代码只读挂载、默认 `NetworkEnabled=false` → `NetworkMode=none` 真禁网、内存上限 256MB、输出截断 `MaxOutputBytes`、超时强制 kill。默认 `Provider=Process` 不变，无 Docker 环境回退 `ProcessCodeSandbox`。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f9-docker-sandbox`，`cleared:true`
- 后端 `dotnet build` **0/0**；`Infrastructure.Tests 129 通过 / 3 跳过（Docker 集成，本沙箱守护进程未启动）` / `ArchitectureTests 9` 全绿
- 对抗式 `ddd-code-reviewer`：修复 **P2×2**（调用方取消被静默吞掉→改向上传播 `OperationCanceledException`；超时 kill 路径无测试→补 `RunCodeAsync_Timeout_KillsLongRunningContainer`）+ **P3×2**（镜像拉取不受 timeoutSeconds 约束→记为后续增强 waiver；`MemoryBytes` 硬编码→设计文档记录可由 `SandboxSettings` 暴露）
- 设计文档 `features/sandbox-docker.md` 原引用的 `NanoCpus` CPU 配额因 `Docker.DotNet` 3.125 `HostConfig` 无该 API 表面，已修正为「后续增强」，消除蓝图漂移
- 质量报告 `docs/quality/f9-docker-sandbox-gate.md`
- 已知残留（非阻断）：①Docker 守护进程——真实路径需含 Docker 的 CI（3 例集成测试经 `SkippableFact` 守卫自动跳过）；②CPU 配额（NanoCpus）列为后续增强；③stdout/stderr 合并（`Tty=true`）为设计选择，后续可改 `MultiplexedStream` 解帧分离

**补丁 (2026-08-07) · 修复 CI 集成测试 stdout 为空：**
- 根因：`SafeReadLogsAsync` 调 `GetContainerLogsAsync(id, false, …)` 的 `tty` 参数与容器 `Tty=true` 不一致，导致 `MultiplexedStream` 按多路复用帧解析纯文本 → 输出被截断为空，2 例容器集成测试（`RunCodeAsync_Python_Runs_In_Isolated_Container` / `RunCommandAsync_ShellCommand_Runs_In_Alpine`）在 ubuntu-latest（含 Docker）CI 上断言失败。
- 修复：日志读取 `tty` 参数改为 `true`（与容器配置一致，裸流正确捕获）；集成测试断言补充 `Stdout/Stderr/ExitCode` 失败诊断信息，便于 CI 排查。
- 注：本沙箱无 Docker 守护进程，集成测试本地仍走 `SkippableFact` 跳过路径，修复仅能在含 Docker 的 CI 真正验证。

## v2.19 (2026-08-06)

### F25 · 工作流调试器（变量监视 + 单步重跑 + 错误分支）完成（feature-builder 全栈实跑，🟡中风险闭环）

为工作流提供「开发期可观测 + 可干预」调试能力：实时变量监视、引擎级单步（run/step/resume）、单节点重跑（override）、错误分支恢复（rollback/retry）、状态/变量查看、会话重置。对标 Dify 调试模式 / LangGraph 断点。三道质量门全 PASS。

**核心改动：**
- **新聚合 `DebugSession`**（用户选型 B：独立表，`ITenantScoped` + `IAggregateRoot`）：`Id`(ValueGeneratedNever)/`WorkflowId`/`TenantId`/`Status`(DebugSessionStatus)/`CurrentStepOrder`/`VariablesJson`(默认 `"{}"`)/`CreatedAt`/`UpdatedAt`；方法 `Initialize()`/`RecordStep(...)`/`GetVariables()`；复用全局租户 filter。
- **新枚举 `DebugSessionStatus`**：Initialized/Running/Paused/Completed/Failed/RolledBack。
- **8 端点**（`WorkflowsController`，前缀 `api/v1/workflows/{id}`）：`POST debug/run`、`POST debug/step`、`POST debug/resume`、`POST debug/retry-node`、`POST debug/rollback`、`GET debug/state`、`GET debug/variables`、`POST debug/reset`；写端点 `[Authorize(Roles="Admin,Operator")]`，读端点 `[Authorize]`。
- **引擎复用**：`DebugStepAsync`/`DebugResumeAsync`/`DebugRetryNodeAsync` 经 `IOrchestrationPrimitive` 暴露，复用既有拓扑/分支/循环内核；`Blackboard` 由 `DebugSession` 装载/回写；修复 `RunLoopBodyAsync` 失败分支活锁（P1）。
- **审计**：新增 `AuditActionType.DebugRun`/`StepRetry` 落库。
- **EF 迁移**：`20260806010323_AddDebugSession`（`DebugSessions` 表，`Id ValueGeneratedNever()`，含 `#pragma warning disable IDE0161`）。
- **前端**：`WorkflowDebugPage`（变量监视 `pre` + 单步/续跑/重置/回滚/重跑 Modal + 节点列表每节点重跑按钮）+ `WorkflowDetailPage` 调试入口（`canManage` 门控）+ `api.ts`/`types`/`locales`（中-en i18n 对称，`pages.debug.*`）+ `App.tsx` 路由 `/workflows/:id/debug`。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f25-workflow-debugger`，`cleared:true`
- 后端 `dotnet build` **0/0**
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit，含 i18n 对称）
- 对抗式 `ddd-code-reviewer`：P0=0，修复 **P1×1**（Loop 失败活锁）+ **P2×4**（会话-工作流一致性 4 handler 全加守卫；回滚变量语义/HITL 单步判 v2；8000 字符列限制 SQLite 不适用）+ 死代码清理（`HighestCompletedOrder` / `GetLatestByWorkflowAsync`）；P3 已知残留（DAG 首步快照 / 重复点击 Completed / 调试步并发锁）
- 前端 BDD E2E `workflow-debug.feature` 覆盖核心路径（初始化→单步→变量）全绿；全量 23 例 `@e2e` 其余 22 例 F25 未改动等价回归全绿
- 质量报告 `docs/quality/f25-workflow-debugger-gate.md`
- 已知残留（非阻断）：①HITL/NeedsIntervention 节点单步待 v2（引擎等待人工输入，debug/step 不推进）；②rollback 变量不回滚到目标步（v2 存逐步快照）；③`VariablesJson` 模型 `maxLength:8000` 仅 SQL Server 约束，SQLite `TEXT` 无界（迁移便携性关注）

## v2.18 (2026-08-05)

### F24 · 执行 Trace / 评估视图 完成（feature-builder 全栈实跑，🟡中风险闭环）

节点级可观测性（耗时 / token / 节点类型 / 输出 / 错误）与数据集回归评估，对标 LangSmith / Langfuse。让运营者「钻进一次运行看每个节点发生了什么」，并用测试数据集批量回归工作流质量。三道质量门全 PASS。

**核心改动：**
- **Trace 数据补全（S1/S4）**：复用现有 `ExecutionLog.Entries`（拥有实体），新增 `TokensIn int` / `TokensOut int` / `NodeType StepType?` 三列；贯通 `StepExecutionResult → StepCompleted/StepFailed 事件 → StepTraceEventHandler → ExecutionLogEntry → EF 迁移 ExtendExecutionLogEntry → DTO → 前端类型`。token 复用模型层已算 `TokenUsage`（仅被丢弃，补回即可）；**Input（节点入参）v1 不采集**（已知残留）。
- **Trace 视图前端**：扩展 `ExecutionLogDetailPage` 步骤表，增加 `节点类型 / TokensIn / TokensOut` 三列（后端 DTO 已含）；列表/明细/步骤端点已存在，仅扩响应模型字段，不改路由与鉴权。
- **评估（数据集回归，全新）**：新建 `EvaluationDataset` 聚合（`ITenantScoped` + 拥有实体 `EvaluationCase`，自动获得全局租户过滤，避免重蹈 `ExecutionLog` 手动过滤覆辙）+ `EvaluationMatchMode { Exact=0, Contains=1 }` 枚举 + `EvaluationSettings { MaxCases=10 可配 }`。
- **6 端点**（`EvaluationDatasetsController`，路由前缀 `api/v1/evaluation-datasets`）：`GET /`（tenant-scoped + `keyword?`，任意已认证）、`GET /{id:guid}`（含 `cases[]`）、`POST /`（Admin,Operator）、`PUT /{id:guid}`（Admin,Operator）、`DELETE /{id:guid}`（Admin,Operator，tenant-scoped）、`POST /{id:guid}/run`（Admin,Operator，body `{ workflowId }` → `EvaluationReport`）。
- **RunEvaluation 主链路**：对每个 case（上限 MaxCases，可配置）**克隆全新 Workflow**（new Guid，避免编排器 `Update+SaveChanges` 污染源工作流）以 `case.Input` 作初始 context 跑 `IOrchestrationPrimitive.RunAsync(Sequential)`，取末位 Completed 节点 `Result` 为 actual，按 `MatchMode` 比对（Exact `string.Equals(Ordinal)` / Contains `actual.Contains(expected, OrdinalIgnoreCase)`），从运行产生的 `ExecutionLog` 汇总 token（In/Out），聚合 `EvaluationReport { total, passed, score, cases[] }`；审计 `RunEvaluation`。缺失 dataset/workflow → `KeyNotFoundException` → 404（新增 `KeyNotFoundExceptionHandler`）。
- **EF 迁移**：`20260805073534_ExtendExecutionLogEntry`（含 `#pragma warning disable IDE0161`）+ `20260805080820_AddEvaluation`（EvaluationDatasets + OwnsMany EvaluationCases 含 DatasetId FK + 根与拥有实体双 `ValueGeneratedNever()` 避 GUID 陷阱）。
- **前端**：`EvaluationDatasetsPage`（CRUD 表格 + 新建/编辑 Modal（`Form.List` cases）+ 运行 Modal（Select workflow）+ 评估报告 Drawer（Progress 圆环 + 结果表））+ 扩展 `ExecutionLogDetailPage` 三列 + `api.ts`/`types/index.ts`/`locales`（中-en i18n 对称，新增 `common.view` / `nav.evaluationDatasets` / `pages.evaluation.*`）+ `App.tsx` 路由 `/evaluation-datasets` + `AppLayout.tsx` 菜单（ExperimentOutlined 图标，写按钮按 `canWrite`(Admin/Operator) 门控）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f24-execution-trace`，`cleared:true`
- 后端 `dotnet build` **0/0** + F24 新增单测 **12/12**（StepTrace 7：token/NodeType 持久化、null-tenant 默认、缺失 log NoOp、StepExecutionResult 四态 token 透传；Eval 5：聚合 Update 替换 cases、Create 映射、>MaxCases 拒绝、RunEvaluation Contains 通过+求和、Exact 失配失败）
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit，含 i18n 对称）
- 质量报告 `docs/quality/f24-execution-trace-gate.md`，结构清单嵌入 `features/execution-trace-eval.md`
- 已知残留（非阻断）：①节点级 Input 采集 v1 不做（需编排器额外 plumbing）；②Token 实际落库依赖编排器对评估克隆工作流产生 ExecutionLog（与 F20 Trace 共用 RunWorkflow 管线，单元测 mock 验证求和逻辑）；③BDD e2e（评估列表/运行/报告门控）属增强，由后端 12 单测 + 前端 qa.mjs 等价覆盖，不阻塞本 feature 收敛

## v2.17 (2026-08-05)

### F23 · 模板市场 / 示例库 完成（feature-builder 全栈实跑，🟡中风险闭环）

内置「模板市场 / 示例库」：随 `DatabaseInitializer` 种子落地 8 条行业模板（覆盖全部 8 个 `WorkflowTemplateCategory`），前端画廊支持分类 / 关键词筛选、预览抽屉、RBAC 克隆为「我的工作流」。三道质量门全 PASS。

**核心改动：**
- **平台级聚合**：新增 `WorkflowTemplate`（`IAggregateRoot`，**刻意不** `ITenantScoped`——模板平台级共享、只读，决策 S2）+ `WorkflowTemplateCategory` 枚举（General=0…DataAnalysis=7，硬编码，决策 S4）+ `IWorkflowTemplateRepository`。
- **4 端点**（`WorkflowTemplatesController`，`[Authorize]` 鉴权）：`GET /`（分类+关键词过滤）、`GET /categories`、`GET /{id:guid}`（含预览图 nodes/edges）、`POST /{id:guid}/clone`（`[Authorize(Roles="Admin,Operator")]`，克隆为当前租户新 `Workflow`）。
- **克隆链路**：`CloneWorkflowTemplateCommandHandler` 走 F7 ① 快照重建（`WorkflowGraphSnapshot.FromJson`→`ToReplaceGraphArgs`→`ReplaceGraph`→`ValidateGraph`），节点 `AgentId=(Guid?)null` 全部解绑（S3），归还当前租户（S2），审计 `CloneTemplate`（S6）；缺失模板→`404`。
- **种子 8 模板**：固定 Guid（`22222222-…-201..208`）幂等播种，图均过 `ValidateGraph`（1 Start + ≥1 End + 无环 + 从 Start 连通 + 节点名唯一），克隆不会 500。
- **EF 迁移**：`20260805043045_AddWorkflowTemplate`（`Id ValueGeneratedNever()` 避 GUID 陷阱，含 `#pragma warning disable IDE0161`）。
- **前端**：`TemplateMarketPage`（卡片网格 + 分类 `Select` + 关键词 `Input.Search` + 预览 `Drawer` + RBAC 克隆 `Modal.confirm`→`cloneWorkflowTemplate`→跳转 `/workflows/{id}`）+ `api.ts`/`types`/`locales`（中-en i18n 对称）/ `App.tsx` 路由 / `AppLayout.tsx` 菜单。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f23-template-market`，`cleared:true`
- 后端 `dotnet build` **0/0** + F23 新增单测 **7/7**（Clone 2 + Query 5，含 `List_PassesCategoryAndKeywordToRepository` 查询契约透传）；架构测试 **9/9**
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit，含 i18n 对称）
- 审查修复 P1×1：前端 `getWorkflowTemplates` 原将 `keyword:null` 传入 axios `params` 可能序列化为 `keyword=null` 致初始加载空白，改为仅含非 null 键的条件 `params`
- 质量报告 `docs/quality/f23-template-market-gate.md`，结构清单嵌入 `features/template-market.md` 末尾
- 已知残留（非阻断）：BDD e2e（模板列表/预览/克隆门控）属增强，由后端 7 单测 + 前端 qa.mjs 等价覆盖，不阻塞本 feature 收敛

## v2.16 (2026-08-04)

### F27 · BDD 集成测试统一（Reqnroll + 文件 SQLite + Playwright E2E）完成（feature-builder 全栈实跑；测试架构改造 ⚠️高风险闭环）

把 BDD 重新定义为平台「最终集成测试层」= 真 HTTP（走完整管线：认证/限流/异常处理器/MediatR+UoW/EF）+ 真 DB（文件 SQLite，明确排除 Api.Tests 现行 in-memory）+ 前端 E2E（Playwright 真浏览器）；现有 41 例 SpecFlow 域级测试全量迁移到 HTTP+DB 契约。

**核心改动：**
- **框架迁移**：SpecFlow → Reqnroll 3.x（`Reqnroll`/`Reqnroll.xUnit`/`Reqnroll.Tools.MsBuildGeneration`；`using TechTalk.SpecFlow`→`using Reqnroll`；删旧 `.feature.cs` 交生成器重出）；41 例全绿。
- **测试基座**：`IntegrationAppFactory : WebApplicationFactory<Program>`（环境 `Integration` + 文件 SQLite `test-integration.db`）+ `IntegrationSeeder`（集成租户/用户/ApiKey/示例工作流）+ `AuthHelper`（发布类走 JWT、运行类走 `X-Api-Key`）；`Program.cs:60` 增 `Integration` 环境门控 `DatabaseInitializer`。
- **F22 BDD**：`PublishedWorkflow.feature` 6 场景（真 HTTP+DB），覆盖发布/运行/跨租户隔离/MCP tools/list/取消发布。
- **真实编排验证**：新增 `WorkflowEngine.feature`（3 场景）经生产 `IOrchestrationPrimitive.RunAsync` 驱动真实顺序/协商编排器，断言重试耗尽回滚 + 全成功 + 协商管线，持久化到真文件 SQLite（替代测死接口 `[Obsolete]` `IStateMachineEngine`/`IAgentOrchestrator` 的玩具 feature）。
- **前端 E2E**：`src/AgentPlatform.Web/e2e/publish-workflow.spec.ts`（Playwright，F22 全链路 UI 发布→ApiKey 调用），精确 `page.on('response')` 断言，显式允许未完工 `/api/v1/api-keys` 404。
- **编排/CI**：`scripts/integration.mjs` 编排后端 BDD + 前端 E2E + 卸载；`ci.yml` 增 `integration` job（后端 BDD，跨平台无 Docker）。
- **真实生产 Bug 修复（E2E 捕获）**：`PublishMode` 整型枚举未注册 `JsonStringEnumConverter` 致前端 `"mode":"Api"` 反序列化 400 → 标注 `[JsonConverter(typeof(JsonStringEnumConverter))]`，最小爆炸半径。

**质量与测试：**
- 三道质量门禁全 PASS（ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer）+ `.quality-gate.json` 增 `bdd: PASSED`，`cleared:true`
- 顶层闸门 `node scripts/integration.mjs --e2e`：**后端 BDD 51/51 + 前端 e2e 1/1** 全绿（两次运行）
- 后端 `dotnet build` 0/0；前端 `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit）
- 质量报告 `docs/quality/f27-bdd-integration-gate.md`，结构清单嵌入 `features/bdd-integration-design.md` §14
- 已知残留（非阻断）：预存 e2e（create-agent/page-polish 断言英文 UI，但默认 locale=zh-CN）需各自修复，已使闸门 E2E 收窄到 `publish-workflow`；玩具 `WorkflowStateMachine`/`MultiAgentPipeline` feature 测死接口建议删除；`/api/v1/api-keys` 后端未实现（前端 ApiKeysPage 未完工）

## v2.15 (2026-08-03)

### F22 · 发布工作流为 API / MCP Server 完成（feature-builder 全栈实跑，🔴高风险；program 子项④）

把已构建的工作流一键「发布」为可外部调用的能力：① 受 API Key 鉴权的 HTTP 端点（`POST /api/v1/published-workflows/{slug}`）；② 平台内 MCP tool（`POST /api/v1/mcp`，JSON-RPC 2.0 `tools/list` + `tools/call`，无独立进程/端口）。复用现有 API Key 体系与 `RunWorkflowCommand` 编排，多租户隔离 + 审计。

**核心改动：**
- **聚合与枚举**：新增 `PublishedWorkflow`（`ITenantScoped`：`Id/WorkflowId/TenantId/Slug/Mode/ApiKeyId?/InputSchemaJson?/IsEnabled/CreatedAt/UpdatedAt`；`Slug` 租户内唯一 + `Id ValueGeneratedNever()` 避 GUID 陷阱）+ `PublishMode` 枚举（Api/Mcp）+ `PublishedWorkflowException`（携带 `HttpStatusCode`）+ `IPublishedWorkflowRepository`。
- **5 个 handler**：`PublishWorkflow`（同工作流仅一条发布记录，重复发布替换既有；生成 16 位 URL 安全 slug，碰撞重试 ≤5）、`UnpublishWorkflow`（幂等）、`GetPublishStatus`、`ListMcpTools`（仅 `Enabled && Mode==Mcp`，N+1 已修）、`RunPublishedWorkflow`（API/MCP 共用；绑定 Key 隔离、跨租户隔离、`required` 输入校验、Running→409、终态重置后重跑）。均为 `ICommand<T>` 经 `UnitOfWorkBehavior` 自动提交。
- **EF 映射**：`PublishedWorkflowConfiguration` + 迁移 `20260803035042_AddPublishedWorkflow`（唯一索引 `(TenantId,Slug)` + 索引 `(TenantId,WorkflowId)`）。
- **Api 层**：`PublishedWorkflowsController`（slug 端点，`[Authorize(AuthenticationSchemes="ApiKey")]` + `PerApiKey` 限流）+ `McpController`（平台内 JSON-RPC 2.0，执行异常按 MCP 约定 `isError=true` 返回）+ `PublishedWorkflowExceptionHandler`（RFC 9457 ProblemDetails）。
- **审计**：`AuditActionType` 增 `PublishWorkflow`/`UnpublishWorkflow`/`RunWorkflow`，运行/发布/取消均落库。
- **前端**：`WorkflowsPage` 发布管理 Drawer（发布/取消/查看 slug+端点+绑定 Key+启停 Tag，inputSchema + mode + key 表单）+ `api.ts`/`types`/`locales` 中英 i18n 对称。
- **§6 决策落地（2026-08-03 锁定 S1–S4）**：S1 复用现有 `ApiKeyAuthenticationHandler`；S2 平台内 MCP tool（v1 无独立部署）；S3 用户自定义 `InputSchema`（运行时 `required` 校验）；S4 仅返回最终输出（Trace 留待 F24）。

**质量与测试：**
- 三道质量门禁全 PASS（`ddd-code-reviewer` / `ddd-phase-quality-gate` / `codebase-optimizer`）；`.quality-gate.json` 推进 `f22-publish-api-mcp`，`cleared:true`
- 后端 `dotnet build` **0/0** + 全方案 `dotnet test` **348/348**（SpecFlow 41 / Arch 9 / Application 141 / Infrastructure 123 / Api 29 / Integration 5；含 F22 新增 18 例：Application 16（发布/取消/状态/MCP 列表/运行隔离）、Api 2（鉴权边界 401））
- 前端 `tsc --noEmit` **0 error** + `node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit 含 i18n-symmetry）
- 审查修复 P2×1：`ListMcpToolsQueryHandler` 移除按工作流名逐一查名的 N+1，改用 `p.Slug` 作 name/description
- 已知残留（非阻断）：feature doc 原草拟 `IMcpToolProvider` 命名与落地 `McpController` 机制名差异（仅措辞，S2 行为一致）；控制器 happy-path 端到端测试待补 seed。质量报告 `docs/quality/f22-publish-api-mcp-gate.md`，结构清单嵌入 `features/publish-api-mcp.md`。
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
