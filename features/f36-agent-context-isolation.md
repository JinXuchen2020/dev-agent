# F36 · Agent 上下文隔离（Blackboard 分区 + 独立对话历史）设计文档

> 来源：F31 Agent 运行时实体化 · D4「Blackboard 按 agent 分区 / 每 agent 独立对话历史延后」；F32 消息总线 ·「明确不做」。
> 风险等级：🟡 中风险（Blackboard 视图语义重构波及编排全链路 + Conversation 加列迁移）。
> 分支：`feat/f36-agent-context-isolation`（2026-08-31 自 `feat/f35-workspace-isolation` 新建——用户指定以 F35 分支为基线，F36 需要消费 F35 的 WorkspaceId 基础设施）。

## 1. 目标

同一工作流内多个 agent 拥有相互隔离的上下文视图：

1. **Blackboard 按 agent 分区**：agent 步骤的上下文注入（prompt 组装读取 Blackboard.Entries）只读自己分区 + 显式共享区，杜绝 agent A 的中间产物无声泄漏进 agent B 的 prompt；非 agent 节点（Variable/Loop/Condition/HTTP/Code）仍走全局共享区（DAG 数据流的根基，不动）。
2. **对话历史按 agent 隔离**：`Conversation` 增加 `AgentId`（nullable）；agent 步骤运行时创建/复用「per-agent per-workflow」的会话并写入本轮 prompt/回复消息；同一工作流内不同 agent 的对话互不可见。
3. **向后兼容**：无 `AgentId` 的存量 Conversation（人工创建/Chat 绑定）回退全局视图，行为与现状一致。

## 2. 代码现状（调研事实，2026-08-31）

| 事实 | 位置 |
|---|---|
| `Blackboard` 是 `Dictionary<string,string>`（**非** backlog 原文的 `<string,object>`），API 仅 `Get`/`Set`（原地写）/`Entries`，无快照/序列化方法 | `Application/Abstractions/WorkflowContext.cs:68-90` |
| Sequential 编排全程**共享单一 Blackboard 引用**（非 clone）；Loop 内联同引用 | `SequentialOrchestrator.cs:86→95→199→590`、`RunLoopBodyAsync:677,687` |
| 直接写键方：Variable 节点（用户自定义键）、Loop itemVariable（裸键如 `x`）、触发器信封（`trigger`/`trigger.*` 前缀，`SeedTriggerBlackboard`） | `VariableStepExecutor.cs:44,51,68`；`SequentialOrchestrator.cs:687,796-819` |
| F33 语义召回**不写 Blackboard**（写 `Summary.Summaries` 负数键） | `SequentialOrchestrator.cs:567-572` |
| `AgentCallStepExecutor` 仅读 `Entries` 注入 prompt（:127-131），**零 Conversation 依赖**（构造器仅 logger+IAgentRepository+IModelRouter） | `AgentCallStepExecutor.cs:29-37,106-157` |
| Blackboard **全量 string→string 序列化**进三个持久化格式：F30 检查点（`ExecutionCheckpoint.Blackboard`，SchemaVersion=1）、F25 调试器（`DebugSession.VariablesJson`）、F30 `RunningExecution.BlackboardSnapshot` | `SequentialOrchestrator.cs:934,960,981-993`；`DebugSession.cs:32,75`；`RunningExecution.cs:49` |
| NegotiationOrchestrator 每次 `BuildWorkflowContext` 新建 `Blackboard.Empty`（黑板丢弃式）；F32 并行提案仅扇出 I/O、写回串行，**当前无黑板并发写** | `NegotiationOrchestrator.cs:279`；`CollaborativeLoop.cs:15-16,58-91` |
| `Conversation` 无 `AgentId`（字段：Id/WorkflowId?/Messages/TotalTokenUsage/Status/KB 绑定/TenantId/WorkspaceId）；创建方仅 `CreateConversationCommandHandler`（人工/Chat），AgentCall 不创建 | `Conversation.cs:11-98`；`CreateConversationCommandHandler.cs:23` |
| 消息链路纯 by id 定位（`SendMessageCommandHandler` → `GetByIdWithMessagesAsync`）；仓储无按 workflow/agent 查询 | `SendMessageCommandHandler.cs:48`；`IConversationRepository.cs:8` |
| 前端 `Conversation.agentName` 字段后端从不回填（卡片标题兜底链 `agentName ?? workflowId ?? id`） | `types/index.ts:97-107`；`ConversationsPage.tsx:70` |
| `ConversationConfiguration` 无任何索引；无 AgentId 列 | `ConversationConfiguration.cs:19-57` |
| 测试影响面：Blackboard 直接依赖单测 10+ 文件；SpecFlow Conversation.feature 7 场景 / WorkflowStateMachine / WorkflowEngine；e2e conversation.feature | 见 §8 |

## 3. 设计（v1 最小闭环，对齐 backlog 意图 + 现实修正）

### 3.1 Blackboard 分区（D1 决策）

**软分区（视图过滤，推荐）**：底层保持扁平 `string→string` 存储与既有持久化格式（F30/F25/RunningExecution 零迁移、SchemaVersion 不动）：

- 键约定：agent 分区键 = `agent:{agentId}:{key}`；无前缀 = 全局共享区（既有行为不变）。
- `Blackboard` 新增 API：`GetPartitionView(Guid agentId)`（返回仅含该 agent 分区 + 全局键的只读视图 `IReadOnlyDictionary<string,string>`）、`SetInPartition(Guid agentId, string key, string value)`。
- `AgentCallStepExecutor` 的 prompt 注入改用 `GetPartitionView(step.AssignedAgentId.Value)`（无 AssignedAgentId 的 LLM 步骤维持全量 Entries，行为不变）；agent 步骤的关键产物（最终回复）以 `agent:{agentId}:output` 写回全局区（供下游步骤/Condition 引用，显式共享而非无声泄漏）。
- 硬分区（`Dictionary<Guid,…>` 全面重构 + 三个持久化格式 SchemaVersion 升级 + 存量迁移）列为 v2，不在本 feature（破坏面与收益不成比例，见 §7 风险）。

### 3.2 Conversation.AgentId（对话隔离落点，D2 决策）

- `Conversation` 加 `Guid? AgentId` + EF 迁移 `AddConversationAgentId`（`Persistence/Migrations` 目录，接 F32 最新迁移；SQLite 默认 NULL）。
- 仓储新增 `GetByAgentAsync(Guid tenantId, Guid workflowId, Guid agentId)`（复用需 workflowId+agentId 双条件；配置 `HasIndex(AgentId)`）。
- `AgentCallStepExecutor`（D2=A 时）：每次 agent 调用 → 无则创建（`new Conversation(id, tenantId, workflowId, agentId)`，经 `IConversationRepository` + `IUnitOfWork` 落库）→ 追加两条消息（`user` = 组装后的 prompt 摘要、`assistant` = 模型回复）→ 复用同一会话（同一工作流内同 agent 的历史累积、跨 agent 天然隔离）。持久化失败不阻断主流程（best-effort + 结构化日志，对齐 F17 溯源先例）。
- 无 `AgentId` 的存量会话：现有创建/发消息/列路径零改动，天然回退全局视图。

### 3.3 Api / 前端

- `ConversationsController` 列表端点加可选 `agentId` 查询参数；`ConversationDto` 补 `agentId`。
- 前端：`types/index.ts` + `api.ts` 补字段与参数；`ConversationsPage` 卡片显示 agent 标签 + agentId 筛选（D3 决策）；i18n 对称。

## 4. 验收标准

1. 同一工作流两个 agent 步骤：A 分区写入的键不出现在 B 的 prompt 注入视图（单测断言）。
2. agent 步骤运行后存在 `Conversation.AgentId = agentId` 的会话且含本轮 prompt/回复消息；同 agent 第二次运行复用同一会话（消息累积、不新建）。
3. 存量无 AgentId 会话的创建/列表/发消息行为不变（SpecFlow Conversation.feature 回归全绿）。
4. Variable/Loop/触发器/Condition/HTTP/Code 节点走全局区行为不变（F20/F25 既有测试回归全绿）。
5. F30 检查点 / F25 调试器 / RunningExecution 快照的持久化格式与恢复行为不变（零 SchemaVersion 变更）。
6. build 0/0 + 全量 `dotnet test` 0 失败（既有豁免清单除外：SpecFlow LLM 用例等）+ 前端 tsc 0 + vitest/vite build 通过。
7. 三道质量门全绿；`.quality-gate.json` 推进 `f36-agent-context-isolation` 含 `cleared:true` + `codebaseOptimizer`；质量报告 `docs/quality/f36-agent-context-isolation-gate.md`。

## 5. 决策（已锁定，2026-08-31 用户拍板）

- **D1 分区语义 = A（软分区视图）**：底层扁平 `string→string` 存储不变；agent 分区键约定 `agent:{agentId}:`；`GetPartitionView(agentId)` 视图过滤；F30/F25/RunningExecution 持久化格式零变更。
- **D2 对话落点 = A（自动建会话）**：`AgentCallStepExecutor` 自动创建/复用 per-agent per-workflow 会话并写入 prompt/回复消息；best-effort（持久化失败不阻断编排，结构化日志）。
- **D3 前端 = A（筛选+标签）**：会话列表 agent 筛选 + 卡片 agent 标签；列表端点加可选 `agentId` 参数。
- **D4 产物回写 = A（显式回写）**：agent 回复写全局键 `agent:{agentId}:output` 供下游显式引用。

- **D1 分区语义**：A) 软分区视图（底层扁平存储 + `agent:{agentId}:` 键约定 + 视图过滤；持久化格式零变更）；B) 硬分区结构重构（三个持久化格式 SchemaVersion 升级 + 存量迁移）。**建议 A**。
- **D2 agent 对话历史落点**：A) AgentCallStepExecutor 自动创建/复用 per-agent 会话并写入 prompt/回复消息（隔离落点完整，但 agent 步骤新增持久化副作用）；B) 仅加列 + 绑定能力，不自动建会话（本 feature 落半截，agent 隔离无实际数据）。**建议 A（best-effort 失败不阻断主流程）**。
- **D3 前端范围**：A) 列表加 agent 筛选 + 卡片 agent 标签；B) 仅卡片标签不加热门筛选。**建议 A**（backlog「可选」项顺手收口）。
- **D4 agent 产物回写**：A) 回复写全局 `agent:{agentId}:output` 供下游显式引用（打破完全黑箱，Condition/下游 LLM 可引用）；B) 不回写全局区（严格隔离，但下游步骤无法引用 agent 输出）。**建议 A**（键名显式、无泄漏歧义）。

## 6. 风险

- 🟡 prompt 注入视图切换改变既有 LLM 步骤行为：无 AssignedAgentId 的 LLM/Critic 步骤保持全量 Entries（零行为变化），仅 agent 步骤收窄——用单测锁定。
- 🟡 agent 步骤新增会话持久化副作用：best-effort 包裹，失败仅记日志不阻断编排；仓储新增方法有租户过滤。
- 🟡 Conversation 加列迁移：SQLite NULL 默认列，安全；存量行 AgentId=NULL 语义=全局。
- 🟢 NegotiationOrchestrator 黑板本就 per-run 新建，不受软分区影响；F32 并行写串行化不受影响。

## 7. 测试计划

- Application：Blackboard 分区视图单测（分区/共享/无 agent 全量）；AgentCallStepExecutor 新增：分区注入断言（A 写的键 B 不可见）、会话创建/复用/消息写入断言、持久化失败不阻断断言。
- Infrastructure：`ConversationRepository.GetByAgentAsync` 租户隔离 + AgentId 过滤 EF 测试（SQLite）。
- SpecFlow：Conversation.feature 增「按 agent 隔离」场景（真 HTTP + 文件 SQLite）；既有 7 场景回归。
- 前端：ConversationsPage 筛选/标签渲染测试 + i18n 对称测试。

## 8. 审查修复记录

ddd-code-reviewer 对抗式审查（2026-09-01，fresh-context 子代理）。调查结论 + 已当场修复项：

| 严重度 | 位置 | 问题 | 处置 |
|---|---|---|---|
| P1 | ConversationConfiguration / AddConversationAgentId 迁移 | `GetByAgentAsync` 建会话无数据库级唯一性保障：并发同 (tenant, workflow, agent) 双步骤（如 Negotiation 扇出、双触发重叠窗口）同时 GetByAgent→null→双 Insert，落双行后 FirstOrDefault 取舍任意，历史分裂 | 已修复：新增唯一过滤索引 `IX_Conversations_TenantId_WorkflowId_AgentId`（`HasFilter("AgentId" IS NOT NULL)`，存量 NULL 行豁免），同步迁移 Up/Down、Designer、ModelSnapshot；冲突方由既有 best-effort 包裹吞掉仅告警。新增 EF 测试 `DuplicateAgentConversation_Insert_Is_Rejected_By_Unique_Index` 锁定 |
| P2 | AgentCallStepExecutorTests | 测试缺口：best-effort 声称不吞 OperationCanceledException 但无用例锁定 | 已修复：新增 `AgentStep_ConversationPersistenceCancellation_IsNotSwallowed`（OCE 穿透持久化包裹 → FailedRetry，非伪装 Success） |
| P2 | api.ts getAgents / ConversationsPage | `getAgents()` 未接 AbortSignal：筛选切换/unmount 后请求不被取消（getConversations/getKnowledgeBases 均已接） | 已修复：getAgents 增加可选 signal 并在页面传 controller.signal |
| P2 | ConversationsPage.handleCreate | 新建失败兜底刷新不携带当前筛选条件，列表与已展示筛选状态不一致 | 已修复：兜底 getConversations 携带 status/q/agentId |

调查后确认无缺陷（记录证据）：

1. **GetPartitionView 键剥离边界**：GUID 文本定长小写、`StringComparison.Ordinal` 前缀匹配，不同 agentId 无前缀互串；自分区键恰剥一次前缀；空键/嵌套 agent: 键均安全（`BlackboardPartitionTests` 锁定）。
2. **AgentOutputKey 与 GetGlobalView**：回写键 `agent:{agentId}:output` 是 agent: 前缀键，**被 GetGlobalView 过滤是设计语义而非缺陷**（D4「显式引用」走 `Blackboard.Get`/`Entries`——Condition 表达式求值、HTTP/Code 均读全量 Entries，可达；同 agent 后续步骤经分区视图见剥离后的 `output`；其他 agent 视图与未绑定 LLM 的 prompt 注入均不可见，杜绝无声泄漏）。`GlobalView_Excludes_All_Agent_Partition_Keys` 测试锁定。截断 12000 < Message.Content 上限 16000，无溢出。
3. **GetConversations agentId 过滤**：`AgentId == filter` 语义下 null 会话天然不匹配，与 q/status 内存过滤叠加正确；SpecFlow F36 场景（命中 + 空结果）通过。
4. **WorkspaceId 注入**：agent 会话经 F35 `InjectWorkspaceIdForAddedEntities` 落当前 scope 工作空间，与编排器其他写路径语义一致，非 F36 缺陷。
5. **i18n 对称**：zh-CN/en-US 均含 agentFilter/agentTag；vitest 仅剩 master 既有豁免 2 例。

验证：`dotnet build AgentPlatform.sln` 0 警告 0 错误；Application.Tests F36 过滤 20/20；Infrastructure.Tests ConversationAgentIsolation 4/4；ArchitectureTests 9/9；SpecFlow（会话与聊天管理 + WorkflowStateMachine）17 中 16 通过，唯一失败为 master 既有豁免的真实 LLM 用例；前端 tsc 0 错、vitest 43 中 42 通过（豁免 2 项既有失败，1 文件级/1 用例级）。

## Quality Gate Checklist

> F36 Quality Gate Checklist（8 类齐全，对齐本 feature 模块：Blackboard 分区视图 / AgentCallStepExecutor / Conversation 聚合+迁移 / 仓储查询 / Api 过滤端点 / 前端会话页 / SpecFlow+单测）。由 ddd-phase-quality-gate 生成并嵌入本 feature 文档（不新建独立文件）。

### 1. Pre-flight Version Audit

- [x] 本 feature 零新增 NuGet 包（无版本锁定动作）
- [x] 涉及 API 面已对照真实代码核实：`Blackboard`（Dictionary<string,string>）、`Conversation` 构造器扩参、`GetConversationsQuery` 记录扩参
- [x] 决策 D1-D4 已锁定（§5），与 backlog 原文差异已记录（软分区 vs 硬分区）
- [x] 基线分支 `feat/f35-workspace-isolation` build 通过后才动手

### 2. BDD Scenarios First

- [x] `Conversation.feature` 新增「会话列表支持按归属 agent 过滤（F36）」场景（命中 + 空结果两段），先于 Api 过滤端点联调锁定契约
- [x] 场景覆盖验收标准 #3/#4 的回归面（既有 7 场景不动，全量回归）
- [x] 边界：AgentId=null 全局会话不匹配任何 agent 筛选（空结果断言）；真实创建/持久化路径由单测补齐（创建/复用/失败不阻断/OCE 穿透）

### 3. DDD Layer Rules

- [x] 接口位置：`GetByAgentAsync` 加在 `Domain/Repositories/IConversationRepository`（仓储接口归 Domain，既有惯例）；`Blackboard` 分区 API 在 `Application/Abstractions/WorkflowContext`
- [x] 实现位置：`ConversationRepository.GetByAgentAsync`（Infrastructure/Persistence/Repositories）、分区视图逻辑在 Application 抽象类内
- [x] Domain 零外部 NuGet 依赖；Application 不引用 Infrastructure；Api 仅经 MediatR + `AddApplication/AddInfrastructure`
- [x] 本 feature 无新接口/新实现类（扩展现有成员，DI 注册确认：`IConversationRepository`→ConversationRepository、`IUnitOfWork`→AppDbContext，均在 Infrastructure/DependencyInjection.cs 既有注册）

### 4. DI Registration Completeness

- [x] `AgentCallStepExecutor` 新增两个注入依赖（IConversationRepository/IUnitOfWork）均为既有 Scoped 注册（line 112 / line 437），无需新增
- [x] `AgentCallStepExecutor` 生命周期 = Scoped（随DbContext scope），无跨请求可变状态
- [x] 构造器扩参后全部既有测试工厂同步更新（CreateSut 五参）

### 5. Configuration-First

- [x] 分区键约定单源：`Blackboard.AgentKeyPrefix`（const "agent:"）+ `AgentOutputKey`（"output" 唯一书写点），无散落字符串字面量
- [x] 消息截断上限 12000 / 回写键截断 8000 均 < Message.Content 上限 16000，注释已说明来源
- [x] 无新增硬编码 GUID / URL / 模型名 / 重试次数（测试播种固定 GUID 属测试常量，豁免）

### 6. EF Core Mapping Sync

- [x] `ConversationConfiguration`：AgentId nullable 属性 + `HasIndex(AgentId)` + 唯一过滤索引 `IX_Conversations_TenantId_WorkflowId_AgentId`（`HasFilter("\"AgentId\" IS NOT NULL")`，双栈 SQLite/PG 均合法，NULL 行豁免存量兼容）
- [x] 迁移 `AddConversationAgentId`：Up/Down 对称（AddColumn→双 CreateIndex / 逆序 DropIndex→DropColumn），Designer 与 ModelSnapshot 三处一致
- [x] `dotnet ef` 迁移链挂接 F32 最新迁移之后；DatabaseInitializer `MigrateAsync` 自动应用
- [x] EF 测试锁定：`DuplicateAgentConversation_Insert_Is_Rejected_By_Unique_Index`（真 SQLite DbUpdateException）

### 7. Concurrency and Lifecycle

- [x] Blackboard 为每次编排 per-run 实例（非 Singleton），分区视图返回新建 Dictionary 只读快照，无共享可变状态
- [x] 会话并发重复创建：唯一过滤索引数据库级兜底（冲突方被 best-effort 包裹吞掉仅告警），不依赖 GetByAgent→Insert 的检查窗口
- [x] best-effort 语义行为化锁定：普通持久化异常吞掉（Success），`OperationCanceledException` 穿透（FailedRetry，非伪装成功）
- [x] 持久化副作用不持有跨请求状态；消息经聚合根 `AddMessage` 写入，非直改集合
- [x] NegotiationOrchestrator 黑板本就 per-run 新建，F32 并行提案仅扇出 I/O，不受分区视图影响

### 8. Cross-Cutting Infrastructure

- [x] Api 过滤端点走 MediatR（`GetConversationsQuery` 扩参），Controller 只注入 IMediator；查询不加 `ICommand<T>`
- [x] 所有新增 async 方法透传 CancellationToken（GetByAgentAsync / PersistAgentConversationAsync / controller ct）
- [x] 实现类 `internal sealed`（AgentCallStepExecutor/ConversationRepository/Handler 均符合）；新增公共 API 均有中文 XML 注释
- [x] 列表端点返回 DTO 聚合经既有映射；`agentId` 可选查询参数向后兼容（缺省行为零变化）
- [x] 前端：`agentId` 筛选纳入 useEffect 依赖 + AbortController 取消（getAgents 补 signal）；新建兜底刷新携带当前筛选；卡片 agent 标签 rowKey=c.id；i18n zh-CN/en-US 对称（agentFilter/agentTag）
- [x] `dotnet build` 0 警告 0 错误；受影响测试全绿（既有豁免清单见 §8）

### Incremental Gate Sequence

```
Module 1: Blackboard 分区视图（WorkflowContext + BlackboardPartitionTests）
  - [x] 代码 + 单测（分区/共享/剥离/GlobalView 排除/空板）绿
Module 2: Conversation 聚合 + 迁移（AgentId 列 + 双索引 + Up/Down/Designer/Snapshot）
  - [x] EF 测试（RoundTrip/唯一索引拒绝）绿
Module 3: 仓储查询 + AgentCallStepExecutor（GetByAgentAsync / 会话创建复用 / 分区注入 / 回写键）
  - [x] 单测（隔离命中/复用不新建/失败不阻断/OCE 穿透/输出键）绿
Module 4: Api 过滤端点 + 前端会话页（agentId 参数 / 筛选下拉 / 标签 / i18n）
  - [x] SpecFlow F36 场景绿 + tsc 0 错
Module 5: 回归收口
  - [x] SpecFlow Conversation.feature 全场景 + WorkflowStateMachine 回归绿（既有豁免除外）
```
