# 会话挂知识库 UI 联动（conversation-kb-linkage）

> 状态：已实现（2026-07-23，质量门 PASS）
> 优先级：P2/P3（RAG 自主配置期第一项，接续 `rag-design.md` §3.1）
> 关联：`features/rag-design.md`（R1–R4 已落地）、`features/backlog.md` §一 R1–R4 与 B5
> 决策来源：用户拍板（AskUserQuestion 三答）——**打通聊天页+联动** / **会话持久化挂载** / **检索走 KB + default 并集**。

## 0. 背景与现状（已核实）

- `Conversation` 聚合（`src/AgentPlatform.Domain/Aggregates/Conversations/Conversation.cs`）**无任何知识库关联字段**；`SendMessageCommand` 仅含可选 `SearchQuery`/`Model`，RAG 仅在传 `SearchQuery` 时检索硬编码 `"default"` 集合（该集合恒为空 → 实为死代码路径）。
- `ConversationsPage` 是**孤儿页面**：未接路由、未进菜单、无详情页、无发消息 UI（即 backlog B5「会话功能不可用」死胡同）。
- `KnowledgeBase` 已有 `CollectionName`（slug+8 位），正是 `SendMessage` 检索所需的集合名。
- R1–R4 已落地（`docs/quality/rag-foundation-gate.md`），`IVectorStore.SearchAsync(string collectionName, string query, Guid tenantId, int topK, double? minScore)` 已支持租户隔离与阈值。

## 1. 范围（用户拍板）

**打通聊天页 + KB 联动一期完成**：
- 接回 `/conversations` 列表路由 + 菜单；列表行可点进详情。
- 新建 `/conversations/:id` 详情聊天页（消息气泡 + 输入框 + 发消息）。
- 详情页内加**知识库选择器**，与会话持久化联动（挂载/解除）。

## 2. 后端设计

### 2.1 会话持久化挂载（聚合层）
`Conversation` 聚合新增：
```csharp
public Guid? KnowledgeBaseId { get; private set; }
public string? CollectionName { get; private set; }
public void AttachKnowledgeBase(Guid knowledgeBaseId, string collectionName) // 非空校验
public void DetachKnowledgeBase()
```
- `ConversationConfiguration` 加两列映射（`CollectionName` 设 `HasMaxLength`）。属标量列，无需 OwnsOne。
- **EF 迁移**：必须新增迁移（项目 `DatabaseInitializer` 以 `MigrateAsync` 为 schema 唯一来源；不迁移则现网/开发 SQLite 拿不到新列）。本次迁移一并补上 R1–R4 遗留的 `KnowledgeBase` 表缺口（此前未生成迁移）。

### 2.2 挂载 / 解除端点
- `PUT api/v1/conversations/{id}/knowledge-base` 请求体 `{ knowledgeBaseId: Guid }`
  - handler 经 `IKnowledgeBaseRepository.GetByIdAsync(kbId)` 解析 `CollectionName`；校验 `kb.TenantId == 当前租户`（跨租户拒绝 → 404/403）；调用 `conversation.AttachKnowledgeBase(kb.Id, kb.CollectionName)`。
- `DELETE api/v1/conversations/{id}/knowledge-base` → `conversation.DetachKnowledgeBase()`。
- `GET api/v1/conversations/{id}`（新增）→ 返回聚合（含 Messages，复用 `GetByIdWithMessagesAsync`），供详情页加载历史消息与当前挂载状态。
- 列表 `GetByTenantAsync` 加 `Include(c => c.Messages)`，使列表「消息数」列有值。
- 鉴权：挂载/解除/发消息均 `[Authorize(Roles="Admin,Operator")]`（与现有 SendMessage 一致）；GET 列表/详情继承类级 `[Authorize]`。

### 2.3 SendMessage RAG 联动（rag-design §2.4 并集语义）
`SendMessageCommandHandler` RAG 块改造：
- 触发条件：`!string.IsNullOrWhiteSpace(request.SearchQuery) || !string.IsNullOrWhiteSpace(conversation.CollectionName)`。
- 检索词：`query = request.SearchQuery?.Trim() ?? request.Content`（无显式 SearchQuery 时用消息正文，实现自动接地）。
- 检索集合：`collections = [RoutingConstants.DefaultVectorCollection]`；若 `conversation.CollectionName` 非空则追加该集合（**KB + default 并集**）。
- 对每个集合调用 `SearchAsync(collection, query, tenantId, topK, minScore)`，合并结果并按 `Content` 去重。
- 命中 >0 条 → 注入为第 2 条 `System` 消息（保持现有文案与前缀）。
- 保留 `try/catch` 降级（单集合异常不影响其余集合；整体失败降级为不使用上下文，不 500）。
- **向后兼容**：无挂载且无 SearchQuery → 不检索（行为不变）；仅传 SearchQuery → 只搜 default（行为不变）。

## 3. 前端设计

- `types/index.ts`：`Conversation` 加 `knowledgeBaseId?: string; collectionName?: string;`。
- `services/api.ts` 加：
  - `getConversation(id)` → `GET /conversations/{id}`
  - `setConversationKnowledgeBase(id, kbId)` → `PUT /conversations/{id}/knowledge-base { knowledgeBaseId }`
  - `removeConversationKnowledgeBase(id)` → `DELETE /conversations/{id}/knowledge-base`
  - `sendMessage(id, content, { searchQuery?, model? }?)` → `POST /conversations/{id}/messages`
- `App.tsx`：导入 `ConversationsPage` + `ConversationDetailPage`；加 `/conversations`、`/conversations/:id` 路由。
- `AppLayout.tsx`：菜单加 `{ key: '/conversations', icon: <MessageOutlined />, label: '会话' }`。
- `ConversationsPage.tsx`：`onRow` 点击 → `navigate('/conversations/'+id)`；加「知识库」列（用已拉取的 KB 列表按 `collectionName` 映射出名称展示）。
- 新建 `ConversationDetailPage.tsx`：
  - 加载会话（消息气泡：User 右 / Agent 左 / System 隐藏）+ 已挂 KB 状态。
  - 底部输入框 + 发送（乐观追加 user 消息，收到回复追加 agent 消息；失败 `Alert` 错误态）。
  - 顶部知识库选择器（`Select` 选项来自 `getKnowledgeBases()`）：选中即 `setConversationKnowledgeBase`，清空即 `removeConversationKnowledgeBase`，本地状态同步。

## 4. 质量门验收清单

- [x] `Conversation` 持久化 `KnowledgeBaseId`+`CollectionName`，`Attach`/`Detach` 方法正确（聚合单测）。
- [x] 挂载端点经 `IKnowledgeBaseRepository` 解析且校验租户（跨租户拒绝测试）。
- [x] `SendMessage` 在会话挂 KB 时自动检索该集合（并集 default），用消息正文作 query；跨租户隔离 + `minScore` 仍生效（handler 单测）。
- [x] 无挂载且无 SearchQuery → 不检索（向后兼容，现有 handler 单测不破）。
- [x] 前端 `/conversations` 列表可点进详情；详情页可发消息 + 挂/解除 KB（qa 4 道闸门全绿）。
- [x] EF 迁移生成且可应用（含 KB 表缺口补丁）；`dotnet build` 0/0；`dotnet test` 全绿；前端 QA 4/4。
- [x] 提交含 `Quality-Gate:` 行，`.quality-gate.json` cleared:true。

## 5. 质量门（ddd-phase-quality-gate · 结构门）

> 由 `ddd-phase-quality-gate` 技能审计（12 类全扫）。**Gate Status: PASS（P0=0 P1=0 P2=0 P3=0）**。

### Phase Quality Gate Checklist（8 类）

1. **Pre-flight Version Audit** — 仅用既有依赖（MediatR / EF Core 9 / Ant Design 5）；无新增 NuGet 包；`IVectorStore.SearchAsync` 签名沿用 R1–R4 已验证契约。
2. **BDD Scenarios First** — 聚合不变量 + handler 行为以 xUnit 单测覆盖（见 §4 验收 + `AgentPlatform.Application.Tests` / `Infrastructure.Tests`）；本特性无新增 SpecFlow 叙事（复用既有 SendMessage/Conversation 契约）。
3. **DDD Layer Rules** — `Conversation` 聚合在 Domain；`*Command/*Query` + handlers 在 Application；`ConversationConfiguration`/`KnowledgeBaseConfiguration` 在 Infrastructure；`IVectorStore` 在 Infrastructure。接口均在 `Application.Abstractions` / `Domain`，实现均在 Infrastructure。
4. **DI Registration Completeness** — 本特性无新增接口；`IConversationRepository`/`IKnowledgeBaseRepository`/`IAuditLogRepository`/`IVectorStore` 均已在 `Infrastructure/DependencyInjection.cs` 注册（Scoped）。MediatR 自动注册 handling（controller → mediator → handler 链路已 grep 确认）。
5. **Configuration-First** — `RagSettings.DefaultTopK`/`DefaultMinScore` 经 `IOptions<RagSettings>` 注入；`RoutingConstants.DefaultVectorCollection` 收口集合名；无硬编码阈值。
6. **EF Core Mapping Sync** — `Conversation` 新列经 `ConversationConfiguration` 映射；`KnowledgeBase`/`KnowledgeDocument` 经 `KnowledgeBaseConfiguration` 映射；新增迁移 `20260723050556_ConversationKnowledgeBase` 同时补 R1–R4 `KnowledgeBase` 表缺口（`DatabaseInitializer.MigrateAsync` 为 schema 唯一来源）。
7. **Concurrency & Lifecycle** — `SendMessageCommandHandler` 的 `HashSet<string>` 为请求内局部变量（非共享）；`InMemoryVectorStore` 维持 prior 评审修复的 `Singleton` + `ConcurrentDictionary`；DbContext Scoped，聚合经 `FindAsync` 加载后由 `UnitOfWorkBehavior.SaveChangesAsync` 提交（与 `CreateConversation` 同模式，链接持久化已验）。
8. **Cross-Cutting Infrastructure** — 鉴权沿用既有：`PUT/DELETE {id}/knowledge-base` 与 `POST {id}/messages` 均 `[Authorize(Roles="Admin,Operator")]`，GET 继承类级 `[Authorize]`；JSON 用 `CamelCase`（前端 `messages`/`knowledgeBaseId`/`collectionName`/`role`/`content` 与 `reply`/`modelId`/`tokenUsage` 均对齐）；Swagger/ProblemDetails 沿用全局。

### 审计发现（12 类）

| Severity | Pattern | File | Finding | Fix |
|----------|---------|------|---------|-----|
| — | DI Gaps / Layer / EF / Hardcode / CT / Modifiers / Concurrency / Null / DeadCode | 全模块 | 全 12 类扫描：0 处违规 | 无需修复 |

- `AuditActionType.UpdateConversation` 已新增并被两 handler 引用（非休眠枚举）。
- 三新 handler 均经 `ConversationsController` 可达（grep 确认），无死代码。

