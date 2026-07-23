# RAG 设计文档（rag-design）

> 状态：**已实现（地基层 R1–R4 已落地并过质量门，2026-07-23；报告 `docs/quality/rag-foundation-gate.md`）**
> 优先级：P2/P3（在「P1 缺陷修复 → 竞品 P0(PUT 端点) → 竞品 P1(DAG 画布)」之后）
> 关联：`features/backlog.md`（缺陷 B1–B6 / O1–O14）、`features/competitive-roadmap.md`（竞品矩阵 RAG 列 🟡）
> 关键结论：**当前 RAG 代码骨架真实可跑，但作为「可用功能」不成立——需先补地基（入库通道 + 租户隔离 + 部署适配 + 相关性阈值），再做「自主配置」UI。**

---

## 0. 一句话结论

RAG 目前是「接了线、亮了灯，但**没通电、没隔离、绑死 Postgres+OpenAI**」的装饰：
- `IVectorStore` 抽象干净，`PgVectorStore` 是真实 pgvector 实现，并被 3 条路径接线；
- 但**没有任何入库通道** → 两个向量集合永远为空 → 检索全部返回 0 → 生产环境是静默 no-op；
- 且**无租户隔离**（多租户下租户互查知识）、**默认 SQLite 部署会 500**、**无相关性阈值**。

「自主配置」（用户建库/传文档/选模型）值得做，但**必须先补上述地基**，且排期在编辑器/DAG 之后。

---

## 1. 当前架构事实（已核实，引用文件）

### 1.1 真实存在的部分（质量不错）
| 组件 | 路径 | 说明 |
|---|---|---|
| `IVectorStore` 抽象 | `src/AgentPlatform.Application/Abstractions/IVectorStore.cs` | `SearchAsync` / `IngestDocumentAsync` / `DeleteCollectionAsync` 三方法，签名干净 |
| `PgVectorStore` 实现 | `src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs` | 真实余弦距离 `1 - (embedding <=> @q)`、懒建表、metadata JSONB、维度注释清晰 |
| 接线点 | `SendMessageCommandHandler`、`SequentialOrchestrator`、`NegotiationOrchestrator` | 均注入 `IVectorStore` 并调用 `SearchAsync` |
| 集合常量 | `src/AgentPlatform.Application/Routing/RoutingConstants.cs` | `DefaultVectorCollection = "default"`；工作流侧用 `"workflow-context"` |

### 1.2 阻断点（按严重度）

#### R1 · 没有入库通道（最致命，P0-blocking）
- 全仓搜 `IngestDocumentAsync`：**调用方只有接口定义 + `PgVectorStore` 实现本身**。
- 没有任何 controller / command handler / background job 调用它。
- 后果：`default` 与 `workflow-context` 两个集合**永远为空** → 三处 `SearchAsync` 全返回 0 条 → 生产环境 RAG 是**静默 no-op**。
- 会话侧还有 `if (docs.Count > 0)` 直接跳过，连报错都没有。
- ⚠️ **质量门教训**：Phase 4 验收标准写「store 能 Ingest/Search/Delete」（验证了 store 本身），但**没验证「有路径在往里入库」**——这正是之前 quality gate 漏掉的 blueprint drift。整改时必须把「入库端点存在且被调用」纳入验收。

#### R2 · 无租户隔离（高严重度，安全/合规）
- `document_embeddings` 表**没有 `tenant_id` 列**；`SearchAsync` 的 WHERE 只按 `collection_name` 过滤。
- 但 Phase 5 已落地**真实多租户**（`AppDbContext.HasQueryFilter`）。
- 后果：多租户下，**租户 A 能检索到租户 B 的知识**。
- `IVectorStore` 接口本身连 `tenantId` 参数都没有 → 是结构性缺失，不是配置问题。

#### R3 · 部署强耦合（默认配置直接崩）
- `DependencyInjection.cs` 中 `PgVectorStore` 是**无条件注册**（`AddScoped<IVectorStore, PgVectorStore>()`）。
- 它要求 `ConnectionStrings:PostgreSQL` + pgvector 扩展 + `OpenAI:Key`（embedding 用）。
- 而默认 `Database:Type = sqlite`。
- 后果：SQLite 部署下 RAG 通道首次触发即抛 `InvalidOperationException("PostgreSQL is not configured")`；**会话路径不 catch → 直接 500**；工作流路径有 try/catch 静默降级（看起来"没接地"）。

#### R4 · 无相关性阈值 + 语义错位
- 对话/工作流都把**全部召回**（不看 `Score`）当「知识库上下文」注入 prompt，低分噪声被一并塞入。
- 工作流侧用 `currentStep.StepName` 搜 `workflow-context`，更像「步骤间上下文复用」，**而非用户理解的「外部知识检索」**——做配置前必须先定清楚：工作流要不要独立的 RAG 节点、检什么。

---

## 2. 整改设计（地基层，非「自主配置」范畴）

### 2.1 入库通道（R1）
新增知识库聚合与入库端点。建议结构：

```
KnowledgeBase (聚合根)
  - Id, TenantId, Name, Description
  - CollectionName (= slug 化名称，唯一)
  - EmbeddingModel (默认取配置 OpenAI:EmbeddingModel)
  - Documents: List<KnowledgeDocument>
        KnowledgeDocument
          - Id, FileName, ContentType, ChunkCount
          - Chunks: List<DocumentChunk>  (入库时切分，含 embedding)
```

**端点契约草稿（待拍板）：**
```
POST   /api/v1/knowledge-bases                  # 建库（返回 collectionName）
POST   /api/v1/knowledge-bases/{id}/documents   # 上传文档 → 切分 → 调 IngestDocumentAsync 入库
GET    /api/v1/knowledge-bases                  # 列表（按 tenant 隔离）
GET    /api/v1/knowledge-bases/{id}             # 详情 + 文档列表
DELETE /api/v1/knowledge-bases/{id}             # 删库（级联删 document_embeddings 该 collection）
```

**切分策略**：默认按 token/字符窗口 + 重叠（如 512 tokens / 64 overlap），后续可配。

### 2.2 租户隔离（R2）
1. `document_embeddings` 表加 `tenant_id` 列（迁移脚本）。
2. `IVectorStore` 接口改造：
   ```csharp
   Task<List<RetrievedDocument>> SearchAsync(
       string collectionName,
       float[] queryEmbedding,
       int topK = 5,
       double? minScore = null,
       string? tenantId = null);   // 新增
   ```
3. `PgVectorStore.SearchAsync` WHERE 加 `AND tenant_id = @tenantId`。
4. 三处调用方传入当前 `TenantProvider.GetTenantId()`。
5. **验收必须含跨租户回归测试**：租户 A 入库后，租户 B 检索必须返回 0。

### 2.3 部署适配（R3）
三选一（建议 ①）：
- **① 按需回退**：`DependencyInjection.cs` 改为条件注册——`Database:Type == postgresql` 且配置了 OpenAI Key 时注册 `PgVectorStore`，否则注册 `InMemoryVectorStore`（实现 `IVectorStore`，进程内 `List<>` + 余弦距离，仅供本地/测试）。会话路径 `SearchAsync` 用 try/catch 包装，失败降级而非 500。
- ② 强制声明 RAG 依赖 Postgres（默认部署直接禁用 RAG 入口，UI 提示「需 Postgres」）。
- ③ 抽象出 `IVectorStoreFactory` 按配置返回实现。

### 2.4 相关性阈值（R4）
- `SearchAsync` 加 `double? minScore` 参数；`PgVectorStore` 在 WHERE 加 `AND 1 - (embedding <=> @q) >= @minScore`（注意 pgvector 距离是余弦距离，相似度 = 1 - 距离，需统一口径）。
- 对话/工作流调用时传入阈值（默认 0.7，可配）。
- 工作流 RAG 语义：明确「`workflow-context` 仅用于步骤间上下文复用，不对外暴露为知识库」；若需「外部知识检索」节点，归入 2.5 的 DAG 节点家族。

---

## 3. 「自主配置」设计（用户可配，竞品 P2/P3 级）

> 前提：2.1–2.4 已落地，RAG 真能用。

### 3.1 用户侧能力（对标 Dify/Coze「知识库检索」）
- **知识库 CRUD**：名称、描述、embedding 模型选择。
- **文档管理**：上传（PDF/Markdown/TXT/HTML）、自动切分、查看切片、删除、重新嵌入。
- **检索参数可配**：topK、相关性阈值 minScore、集合（知识库）选择。
- **接入点**：
  - 对话：用户可在会话/助手配置里挂知识库（前端传 `SearchQuery` + `collectionName`，修复 B5 的死胡同）。
  - 工作流：新增 **「知识检索」节点类型**（属 DAG 节点家族，见 competitive-roadmap P1），节点配置 topK/minScore/知识库。

### 3.2 前端页面草案
```
src/AgentPlatform.Web/src/pages/KnowledgeBasesPage.tsx        # 列表 + 新建
src/AgentPlatform.Web/src/pages/KnowledgeBaseDetailPage.tsx   # 文档管理 + 参数
(api.ts) getKnowledgeBases / createKnowledgeBase / uploadDocument / deleteKnowledgeBase
```
> 注意：前端目前**无任何知识库相关代码**（连 `types` 里都没有），属从零新增。

### 3.3 配置项（appsettings）
```json
"Rag": {
  "EmbeddingModel": "text-embedding-3-small",
  "DefaultTopK": 5,
  "DefaultMinScore": 0.7,
  "ChunkSizeTokens": 512,
  "ChunkOverlapTokens": 64
}
```

---

## 4. 排期与依赖（与既有路线图对齐）

```
① 清 P1 缺陷        B1–B5 + O1/O2        → 核心流程"能用"
② 竞品 P0          PUT /workflows/{id}  → 修 B1/B2/B3/B4（Editor 体感）
③ 竞品 P1          DAG 可视化画布 MVP    → 配套后端 Node/Edge 模型
─────────────────── RAG 整改起点（本文档） ───────────────────
④ RAG 地基          R1 入库通道 / R2 租户隔离 / R3 部署适配 / R4 阈值
                   → 让 RAG 真能用、不泄漏、不崩
⑤ RAG 自主配置     知识库 CRUD + 文档 UI + 知识检索节点（前端从零）
⑥ O 系列加固        拆包(O6)/单测(O7)/鉴权一致性(O3/O4)
⑦ Phase 6 收尾      Code Agent 沙箱 / Research Agent（跑在稳固 RAG + 编排核心上）
```

**为什么排在编辑器/DAG 之后**：RAG 自主配置是「建在编排核心之上的大模块」，而当前核心编辑链路断裂（B1）、实时失效（B2）、会话死胡同（B5）。先修核心，RAG 才有承载面；且 Phase 6 的 Research Agent 本质依赖「可用的 RAG + 工作流」。

---

## 5. 质量门验收清单（避免重蹈 Phase 4 覆辙）

实现后 `.quality-gate.json` 验收**必须包含**：
- [x] `IngestDocumentAsync` 有**运行时调用路径**（`KnowledgeBasesController.POST {id}/documents` → `UploadDocumentCommandHandler` 调用），端点可触发；
- [x] 入库后 `SearchAsync` 能返回 >0 条（`InMemoryVectorStoreTests` 覆盖；`VectorStoreFactoryTests` 回退路径 `SearchAsync` 不抛）；
- [x] **跨租户回归**：租户 A 入库，租户 B 检索返回 0（`InMemoryVectorStoreTests.CrossTenantIsolation` + `PgVectorStore`/`IVectorStore` 加 `tenantId`）；
- [x] SQLite 默认部署下，RAG 触发**不 500**（`IVectorStoreFactory` 回退 `InMemoryVectorStore` + `SendMessageCommandHandler` 检索 `try/catch` 降级）；
- [x] 低分噪声被 `minScore` 过滤（断言低分文档不进结果；`InMemoryVectorStoreTests.MinScoreFilter`）；
- [x] `dotnet build` + `dotnet test` 全绿（`build 0/0`；`test 182 passed / 0 failed`，含上述新增 23 例 RAG 单测）。

---

## 6. 待拍板决策（高风险，feature-dev 会停下问人）

1. **入库端点形态**：知识库聚合是否独立 `KnowledgeBase` 实体，还是复用现有 `AgentConfiguration`？建议独立。
2. **切分策略默认值**：512/64 还是按模型上下文动态？
3. **部署适配选 ①/②/③**（本文档建议 ① InMemory 回退）。
4. **工作流 RAG 语义**：`workflow-context` 保留为内部复用，外部知识检索走新节点——确认。
5. **前端是否随后端一期做**：本文档 3.2 前端从零，需确认排期。

---

*本设计文档遵循项目约定：新 feature 先放 `features/` 设计，实现再从 `backlog` 池顶取任务；涉及接口契约/多租户/路由的高风险改动，feature-dev 会先停下确认上述决策。*
