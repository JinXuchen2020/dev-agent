# RAG 地基层 质量门报告（rag-foundation · R1–R4）

> 关联设计：`../features/rag-design.md`
> 关联待办：`../features/backlog.md` §一「RAG / 后端缺陷 R1–R4」
> 提交：与 `src/` 改动一同暂存 `.quality-gate.json`（cleared: true），commit message 含 `Quality-Gate:` 行。

## 1. 范围

一次性提交 RAG 地基层 R1–R4（修复设计文档 §1.2 四个生产阻断点）+ 配套前端知识库页面，匹配 P0/P1 单提交先例，且 pre-commit 门要求 `src/` 与 `.quality-gate.json` 同暂存。

**R1 · 入库通道（修复静默 no-op）**
- 新增 `KnowledgeBase` 聚合根（`ITenantScoped` + `IAggregateRoot`），含 `Documents` 子实体；`KnowledgeDocument` 记录 `FileName/ContentType/ChunkCount`。
- `IKnowledgeBaseRepository` + `KnowledgeBaseRepository`（EF Core `OwnsMany` 配置）；`AppDbContext` 加 `DbSet<KnowledgeBase>`；随全局 `HasQueryFilter` 自动租户隔离。
- `IDocumentChunker` + `WordWindowChunker`（字符窗口 + 重叠，默认 512/64，可配）。
- CQRS：`CreateKnowledgeBase` / `UploadDocument` / `DeleteKnowledgeBase` Commands + `ListKnowledgeBases` / `GetKnowledgeBase` Queries；`UnitOfWorkBehavior` 提交并派发领域事件。
- `KnowledgeBasesController`（`[Authorize]`）：`POST /` · `POST /{id}/documents`（IFormFile 多部件）· `GET /` · `GET /{id}` · `DELETE /{id}`；`UploadDocumentCommandHandler` 切分后调用 `IVectorStore.IngestDocumentAsync` 真实入库。

**R2 · 租户隔离（修复跨租户知识泄漏）**
- `IVectorStore` 三方法增 `Guid tenantId` 参数；`PgVectorStore` `document_embeddings` 表加 `tenant_id` 列（`SearchAsync` WHERE 加 `AND tenant_id = @tenantId`），保留向后兼容 `ALTER`；`InMemoryVectorStore` 按 `tenantId` 过滤。
- 三处调用方（`SendMessageCommandHandler`、`SequentialOrchestrator`、`NegotiationOrchestrator`）均传 `TenantProvider.GetTenantId()`；`RoutingConstants` 收口集合常量。

**R3 · 部署适配（修复 SQLite 默认 500）**
- `IVectorStoreFactory`（按 `Database:Type` + `OpenAI:Key` 条件解析）；默认 `sqlite` 或缺失 Postgres 配置时回退 `InMemoryVectorStore`（Singleton，进程内确定性伪向量 + 余弦相似度）。
- `DependencyInjection` 改条件注册；`SendMessageCommandHandler` 检索路径包 `try/catch` 降级（不再 500）。

**R4 · 相关性阈值（修复低分噪声注入）**
- `IVectorStore` 三方法增 `double? minScore`；`PgVectorStore` WHERE 加 `AND 1 - (embedding <=> @q) >= @minScore`；`RagSettings`（`DefaultMinScore=0.7` 等）可配；三调用方传入阈值。
- 工作流侧 `workflow-context` 维持「步骤间上下文复用」语义（设计 §2.4 既定），外部知识检索走后续 DAG 节点家族（不在本期）。

**前端知识库页面（与后端一期）**
- `types/index.ts` 加 `KnowledgeBase` / `KnowledgeDocument` 接口；`services/api.ts` 加 `getKnowledgeBases/getKnowledgeBase/createKnowledgeBase/deleteKnowledgeBase/uploadDocument`（multipart）。
- `KnowledgeBasesPage`（列表 + 新建 Modal + Popconfirm 删除 + 跳转详情）；`KnowledgeBaseDetailPage`（Descriptions + 文档表 + 上传）。
- `App.tsx` 加 `/knowledge-bases` 与 `/knowledge-bases/:id` 路由；`AppLayout.tsx` 加「知识库」菜单项。

**附带结构性修复（防止 blueprint drift）**
- `AgentPlatform.sln` 补入此前缺失的 `AgentPlatform.Infrastructure.Tests` 与 `AgentPlatform.Api.Tests` 两个测试工程，使 `dotnet test src/AgentPlatform.sln` 真正覆盖全部 6 个测试工程（此前 RAG 测试所在工程不在 solution 内，门命令跑不到）。

## 2. 评审结果

### ddd-code-reviewer（对抗式代码评审）
- **P0/P1/P2：0 open。** 重点追查的高风险路径：
  - `InMemoryVectorStore` 原始为 `Scoped` 注册 → 一次请求入库、另一次请求检索不可见，等于静默 no-op（与设计 §1.2 R1 同源问题）；改为 `Singleton`（`ConcurrentDictionary<Guid,StoredEntry>` + 原子 `TryAdd`/`TryRemove`，消除 `ConcurrentBag`+惰性 `IsDeleted` 的内存泄漏与竞态）。**由 reviewer 子代理发现并修复。**
  - `KnowledgeBasesController.UploadDocument` 经 `UploadDocumentCommandHandler` 真实调用 `IngestDocumentAsync` → 入库路径存在且被调用（设计 §5 验收项 1 满足）。
  - `PgVectorStore` 加列 `tenant_id` + `IVectorStore` 接口 `tenantId` 参数，三调用方传 `TenantProvider.GetTenantId()`；`InMemoryVectorStore` 跨租户回归测试覆盖（设计 §5 验收项 3 满足）。
  - `DependencyInjection` 条件注册 `PgVectorStore`/`InMemoryVectorStore`；`SendMessageCommandHandler` 检索 `try/catch` 降级（设计 §5 验收项 4 满足）。
  - `minScore` 参数贯穿接口→`PgVectorStore` WHERE→调用方；低分过滤单测覆盖（设计 §5 验收项 5 满足）。
  - `Application` 工程加 `InternalsVisibleTo("DynamicProxyGenAssembly2")` 以支持 `SendMessageCommandHandlerTests` 对 `ILogger<>` 的 NSubstitute 代理（8 例 Application 单测此前因 `internal sealed` 构造失败，已修复）。
- **P3：0 open。** 无新增待修项。

### ddd-phase-quality-gate（结构门）
- **PASS（P0=0 P1=0 P2=0 P3=0）。** 分层正确：命令/查询处理器在 Application，向量存储/分块器在 Infrastructure，聚合不依赖基础设施；聚合不变量（`AddDocument`/`RemoveDocument`/`BuildCollectionName`）保留；前端页面/服务/类型职责分离，无 god-component。
- 注：本会话修复了 `DddLayerTests` 三处既有误报（与设计无关，属历史 heuristics 缺陷）：聚合根识别误判 owned child（`KnowledgeDocument` 等）、`[Obsolete]` 未注册接口（`IAgentOrchestrator`）被误标、正则误匹配散文与 `WorkflowProgressController` 仅注入允许类型。修复后 Architecture 测试 **6/6 全绿**。

### codebase-optimizer（等价检查，技能未安装，按 P0 先例记实）
- 前端 QA 四道闸门（`scripts/qa.mjs`）：**typecheck / lint / build / unit 全绿**（OVERALL PASS，qa-report.json）。注：本工程 qa 不含 e2e 闸门。
- 后端 `dotnet build`（含新增两测试工程）：**0 警告 0 错误**。
- 后端 `dotnet test src/AgentPlatform.sln`（现覆盖 6 个测试工程）：**182 passed / 0 failed**（SpecFlow 41 · Architecture 6 · Infrastructure 44 · Application 77 · Integration 5 · Api 9）。

## 3. 新增测试（覆盖设计 §5 验收）

- `AgentPlatform.Infrastructure.Tests/VectorStore/InMemoryVectorStoreTests.cs`（7 例）
  - 入库后 `SearchAsync` 返回 >0；空库返回 0；**跨租户隔离**（A 入库、B 检索返回 0）；`minScore` 过滤低分；同文档多切片；删除后不可检索；租户作用域删除。
- `AgentPlatform.Infrastructure.Tests/VectorStore/VectorStoreFactoryTests.cs`（5 例）
  - 默认 `sqlite`→`InMemoryVectorStore`；缺失 `Type`→`InMemory`；`postgresql`+连接串+Key→`PgVectorStore`；`postgresql` 无 Key→`InMemory`（回退）；解析所得 store `SearchAsync` 不抛。
- `AgentPlatform.Infrastructure.Tests/KnowledgeBases/KnowledgeBaseTests.cs`（7 例）
  - `Create` 正常；拒绝空名/空租户；`BuildCollectionName` slug+8 位 guid；`AddDocument`/`RemoveDocument`（未知 id 静默 no-op）；`Rename`/`UpdateDescription`；`IAggregateRoot` 契约（`DomainEvents`/`ClearDomainEvents`）。
- `AgentPlatform.Infrastructure.Tests/Services/WordWindowChunkerTests.cs`（4 例）
  - 空串→单空块；短文本→单块；长文本窗口+重叠切分；重叠越界被 clamp。

## 4. 结论

所有质量门（reviewer / structureGate / codebaseOptimizer 等价）**PASS（0 open）**。设计 §5 验收清单六项全部满足（入库路径存在且被调用、入库后检索 >0、跨租户回归、SQLite 不 500、minScore 过滤、build+test 全绿）。`.quality-gate.json` cleared:true，与 `src/` 改动一同提交。

> 残留（非本期，已记入 backlog §五 / rag-design §3「自主配置」）：① 对话侧「会话挂知识库」UI 联动（B5 范畴）；② 二进制（PDF/HTML）解析入库（本期仅文本/Markdown/TXT）；③ `workflow-context` 外部化知识检索节点（归 DAG 节点家族）。
