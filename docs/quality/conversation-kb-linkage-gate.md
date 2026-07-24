# 会话挂知识库 UI 联动 质量门报告（conversation-kb-linkage）

> 关联设计：`../features/conversation-kb-linkage.md`
> 关联待办：`../features/backlog.md` §一 B5、`../features/rag-design.md` §3.1「自主配置」
> 提交：与 `src/` 改动一同暂存 `.quality-gate.json`（cleared: true），commit message 含 `Quality-Gate:` 行。

## 1. 范围

一次性提交「会话挂知识库 UI 联动」（用户拍板三答：打通聊天页+联动 / 会话持久化挂载 / 检索走 KB + default 并集），同时补 R1–R4 遗留的 `KnowledgeBase` 表 EF 迁移缺口。匹配 P0/P1 单提交先例，pre-commit 门要求 `src/` 与 `.quality-gate.json` 同暂存。

**后端：会话持久化挂载（聚合层）**
- `Conversation` 聚合新增 `KnowledgeBaseId?` / `CollectionName?` 标量字段 + `AttachKnowledgeBase(kbId, collectionName)`（非空/`Guid.Empty` 校验）/`DetachKnowledgeBase()`；`ConversationConfiguration` 加两列映射（`CollectionName` 设 `HasMaxLength(120)`）。
- `ConversationRepository.GetByTenantAsync` 加 `Include(c => c.Messages)`，使列表「消息数」列有值。
- 新增 CQRS：`SetConversationKnowledgeBaseCommand` / `RemoveConversationKnowledgeBaseCommand` / `GetConversationByIdQuery` + handlers；`SetConversationKnowledgeBaseCommandHandler` 经 `IKnowledgeBaseRepository.GetByIdAsync` 解析 `CollectionName` 并校验 `kb.TenantId == 当前租户`（跨租户抛 `InvalidOperationException`）。
- `ConversationsController` 新增 `GET {id:guid}`（复用 `GetByIdWithMessagesAsync`）、`PUT {id}/knowledge-base`（Admin,Operator）、`DELETE {id}/knowledge-base`（Admin,Operator）；`POST {id}/messages` 不变。

**后端：SendMessage RAG 联动（rag-design §2.4 并集语义）**
- 触发条件：`!string.IsNullOrWhiteSpace(SearchQuery) || !string.IsNullOrWhiteSpace(conversation.CollectionName)`。
- 检索词：`query = SearchQuery?.Trim() ?? Content`（无显式 SearchQuery 时用消息正文 → 自动接地）。
- 检索集合：`[RoutingConstants.DefaultVectorCollection]`；`conversation.CollectionName` 非空则追加（**KB + default 并集**），逐集合 `SearchAsync` 合并并按 `Content` 去重，`try/catch` 降级（单集合异常不影响其余，整体失败不 500）。
- 向后兼容：无挂载且无 SearchQuery → 不检索；仅传 SearchQuery → 只搜 default。

**前端：聊天页打通 + KB 联动**
- `types/index.ts`：`Conversation` 加 `knowledgeBaseId?` / `collectionName?`。
- `services/api.ts` 加 `getConversation` / `setConversationKnowledgeBase` / `removeConversationKnowledgeBase`；`sendMessage` 入参加 `options?{searchQuery?, model?}`。
- `App.tsx` 加 `/conversations` 与 `/conversations/:id` 路由；`AppLayout.tsx` 菜单加「会话」。
- `ConversationsPage.tsx`：行可点进详情、加「知识库」Tag 列（按 `collectionName` 映射 KB 名）。
- 新建 `ConversationDetailPage.tsx`：消息气泡（User 右/Agent 左/System 隐藏）+ 输入框 + 发消息（乐观追加、失败 Alert）；顶部知识库 `Select`（选中→挂载、清空→解除，本地状态同步）。

**EF 迁移（补 R1–R4 缺口）**
- `20260723050556_ConversationKnowledgeBase`：`Conversations` 加 `CollectionName`/`KnowledgeBaseId` 列；**新增 `KnowledgeBases` 与 `KnowledgeDocument` 表**（此前 R1–R4 加聚合却漏生成迁移，现网/开发 SQLite 拿不到表）。`DatabaseInitializer.MigrateAsync` 为 schema 唯一来源，迁移后真实落库。

## 2. 评审结果

### ddd-code-reviewer（对抗式代码评审）
- **P0/P1/P2：0 open。** 重点追查的高风险路径：
  - **写路径持久化（最高风险）**：`SetConversationKnowledgeBaseCommandHandler` 经 `IConversationRepository.GetByIdAsync`（`FindAsync` 加载、被 DbContext 跟踪）修改聚合后返回；`UnitOfWorkBehavior.SaveChangesAsync` 提交所有 change-tracked 聚合 → 链接持久化，与已验证的 `CreateConversationCommandHandler`（同样不显式 `Update`）同模式。已确认无显式 `Update()` 调用也能落库。
  - **JSON 大小写对齐（前端空白屏风险）**：`Program.cs` 配置 `JsonNamingPolicy.CamelCase`；`SendMessageResponse(Reply,ModelId,TokenUsage)` 序列化为 `reply`/`modelId`/`tokenUsage`，`Conversation` 序列化为 `messages`/`knowledgeBaseId`/`collectionName`/`role`/`content` → 与前端 `api.ts`/`ConversationDetailPage`/`ConversationsPage` 完全对齐，无 undefined 空白屏。
  - **SendMessage RAG 并集**：`HashSet<string>(OrdinalIgnoreCase)` 去重（允许 null，且无 `IndexOutOfRange`）；注入为第 2 条 System 消息（位于 system prompt 之后、user 之前）；`try/catch` 整体降级不 500；「KB 集合无文档」时 `SearchAsync` 返回 0 → 不注入（正确）；注入的上下文**不持久化为 Message 实体**（仅当次请求 transient），重载后不显示。
  - **RBAC 一致性**：挂载/解除/发消息均 `[Authorize(Roles="Admin,Operator")]`，与既有 `SendMessage` 一致；GET 继承类级 `[Authorize]`。
- **P3：0 open。** （附注：`GetByTenantAsync` 为支持「消息数」列 `Include(Messages)` 会随列表返回全量消息正文，属有意权衡，非缺陷；列表端点本就返回 `Conversation` 聚合。）

### ddd-phase-quality-gate（结构门）
- **PASS（P0=0 P1=0 P2=0 P3=0）。** 12 类审计全扫：DDD 分层正确（聚合 Domain / handler Application / config·store Infrastructure）；`IConversationRepository`/`IKnowledgeBaseRepository`/`IAuditLogRepository`/`IVectorStore` 均已在 `Infrastructure/DependencyInjection.cs` 注册；MediatR 自动注册 handler（controller→mediator→handler 链路 grep 确认）；EF 映射三聚合齐全且已生成迁移；`RagSettings`/`RoutingConstants` 配置驱动；并发仅局部 `HashSet`；`AuditActionType.UpdateConversation` 已被两 handler 引用（非休眠枚举）；三新 handler 均可达（无死代码）。8 类 checklist 已嵌入 `features/conversation-kb-linkage.md` §5。

### codebase-optimizer（等价检查）
- 前端 QA 四道闸门（`scripts/qa.mjs`）：**typecheck / lint / build / unit 全绿**（OVERALL PASS，qa-report.json）。注：本工程 qa 不含 e2e 闸门。
- 后端 `dotnet build`（含新增两测试工程）：**0 警告 0 错误**。
- 后端 `dotnet test src/AgentPlatform.sln`（覆盖 6 个测试工程）：**192 passed / 0 failed**（SpecFlow 41 · Architecture 6 · Infrastructure 49 · Application 82 · Integration 5 · Api 9）。

## 3. 新增测试（覆盖设计 §4 验收）

- `AgentPlatform.Infrastructure.Tests/Conversations/ConversationKnowledgeBaseTests.cs`（5 例）
  - `Attach` 设置字段；`Detach` 清空；拒绝 `Guid.Empty`；拒绝空/空白 `collectionName`；`Attach` 覆盖原链接。
- `AgentPlatform.Application.Tests/Handlers/SendMessageCommandHandlerTests.cs`（+2 例）
  - `Handle_Should_Ground_In_Linked_KnowledgeBase_With_Content_As_Query`：会话挂 KB 时并集检索 default+kb-collection，以正文为 query，KB 内容注入 context。
  - `Handle_Should_Not_Search_When_No_Kb_And_No_SearchQuery`：向后兼容，不调用 `SearchAsync`，仅 1 条 system（prompt）。
- `AgentPlatform.Application.Tests/Handlers/SetConversationKnowledgeBaseCommandHandlerTests.cs`（3 例）
  - 挂载成功（设 `KnowledgeBaseId`+`CollectionName`）；跨租户拒绝（`InvalidOperationException`，链接保持 null）；会话不存在抛 `ArgumentException`。

## 4. 结论

所有质量门（reviewer / structureGate / codebaseOptimizer 等价）**PASS（0 open）**。设计 §4 验收清单七项全部满足（聚合字段+方法、挂载端点租户校验、SendMessage 并集自动接地、向后兼容、前端聊天页+挂/解除、EF 迁移含 KB 表补丁、build+test 全绿 + QA 4/4）。`.quality-gate.json` cleared:true，与 `src/` 改动一同提交。

> 残留（非本期，已记入 backlog / rag-design §3）：① 文档上传仅文本/Markdown/TXT（PDF/HTML 解析未做）；② 「知识检索」工作流节点（归 DAG 节点家族，rag-design §3.1 工作流接入点）；③ 发消息/挂载 RBAC 限 Admin/Operator（与既有 SendMessage 一致，如需放开须用户拍板）。
