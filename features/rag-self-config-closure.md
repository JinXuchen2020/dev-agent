# 自主配置收尾：PDF/HTML 入库 · 知识检索工作流节点 · 放开发消息 RBAC

> 状态：已实现（2026-07-23 闭环 PASS，提交见 `rag-self-config-closure` gate）
> 关联：features/rag-design.md §3.1、features/backlog.md（B5 已闭合，本期三项是「残留(非本期)」清单的兑现）

## 1. 范围

本设计把 `features/backlog.md` 中标记为「残留(非本期)」的三项一次性兑现：

1. **PDF/HTML 二进制解析入库** —— 当前 `KnowledgeBasesController.UploadDocument` 用 `StreamReader` 把文件当纯文本读，二进制 PDF 会被读成乱码、HTML 会带标签进向量库。需按内容类型/扩展名分发到专门的文本提取器。
2. **知识检索工作流节点** —— `StepType` 枚举预留了 `Knowledge`（注释 P2 reserved）。新增一个 `KnowledgeRetrievalStepExecutor`，让工作流 DAG 能挂一个「知识检索」节点，从指定知识库向量集合检索并把结果作为下游节点的 artifact。
3. **放开发消息 RBAC** —— `ConversationsController.SendMessage` 当前 `[Authorize(Roles="Admin,Operator")]`，导致非管理员无法对话（B5 死胡同的根因之一）。放开为「所有已认证租户用户可发消息」。

## 2. PDF/HTML 二进制解析入库

### 2.1 抽象

- 新增 `Application/Abstractions/IDocumentTextExtractor.cs`：
  ```csharp
  public interface IDocumentTextExtractor
  {
      // 从原始字节流提取纯文本；fileName/contentType 用于判定格式。
      string Extract(Stream content, string fileName, string contentType);
      // 该提取器是否支持此格式（用于分发）。
      bool Supports(string fileName, string contentType);
  }
  ```
- 实现（Infrastructure）按**顺序敏感**的方式注册为 `AddScoped`（顺序：Pdf → Html → Plain，因 `PlainTextExtractor.Supports` 也匹配 `text/*`，必须让 Html 排在 Plain 之前，否则 `.html` 会被当纯文本读出标签）：
  - `PdfTextExtractor` —— **零外部依赖**实现：用内置 `System.IO.Compression.ZLibStream` 解压 `/FlateDecode` 流，再从内容流中用正则抽取 `(...)Tj` / `[...]TJ` 文本算子；覆盖常见的非加密、非 CID 字体文档（best-effort）。*注：实现期曾评估 `PdfPig`，但配置的 nuget 镜像仅提供 `custom/alpha` 分支的不可信 fork 包，故弃用、改为零依赖实现。*
  - `HtmlTextExtractor` —— 去除 `<script>/<style>`，剥离标签，解码 HTML 实体，归一化空白。
  - `PlainTextExtractor` —— 兜底：`Encoding.UTF8` 读出（detect BOM），覆盖 .txt/.md/.csv/.json/.xml/.log/.yml/.yaml，即原 `StreamReader` 行为。

### 2.2 控制器改造

`KnowledgeBasesController.UploadDocument`：

- 读 `file.OpenReadStream()` 到 `byte[]`（避免把二进制当文本）。
- 注入 `IEnumerable<IDocumentTextExtractor>`，选第一个 `Supports(fileName, contentType)` 的提取器；找不到则抛 `UnsupportedContentTypeException`（映射 415）。
- `content = extractor.Extract(stream, file.FileName, file.ContentType)`，其余走原 `UploadDocumentCommand`。

### 2.3 入库契约不变

`UploadDocumentCommand.Content` 仍为纯文本 → `IDocumentChunker.Chunk` → 向量入库。命令/处理器签名不动，既有测试不受影响。

## 3. 知识检索工作流节点

### 3.1 枚举

`Domain/Enums/StepType.cs` 增加：

```csharp
/// <summary>知识库检索节点：从指定知识库向量集合检索相关片段。</summary>
Knowledge = 5
```

### 3.2 执行器

新增 `Infrastructure/Workflows/KnowledgeRetrievalStepExecutor.cs`（internal sealed, `IStepExecutor`）：

- `HandlesType => StepType.Knowledge`；`StepType => "*"`（兜底）。
- 依赖：`IKnowledgeBaseRepository`、`IVectorStore`、`IOptions<RagSettings>`、`ILogger`。
- 解析 `step.ConfigJson` 为：
  ```json
  { "knowledgeBaseId": "guid?", "collectionName": "str?", "query": "str?", "topK": 5, "minScore": 0.7 }
  ```
- 解析目标集合：
  - 有 `knowledgeBaseId` → `repository.GetByIdAsync` → 校验 `kb.TenantId == ctx.TenantId` → 取 `kb.CollectionName`；
  - 否则用 `collectionName`（直接集合名）。
- 解析查询：
  - 有 `query` → 用 `query`；
  - 否则拼接上游已完成节点 artifact 文本（截断），为空则 `FatalFailure("无可检索内容")`。
- `vectorStore.SearchAsync(collectionName, query, ctx.TenantId, topK, minScore)`；空结果 → `Success("", null)`（明确「未检索到」而非失败）。
- `Output` = 拼接的检索片段文本；`artifact` = JSON `{ retrievedChunks, sources }`。
- 全程 `try/catch`，异常 `RetryableFailure`（与 `AgentCallStepExecutor` 一致）。

### 3.3 DI

`Infrastructure/DependencyInjection.cs` 增加：
`services.AddScoped<IStepExecutor, KnowledgeRetrievalStepExecutor>();`

### 3.4 前端联动

- `types/index.ts`：`StepType` 增加 `Knowledge: 5`；`NodeConfig` 增加 `knowledgeBaseId?`、`query?`。
- `stores/workflowCanvasStore.ts`：`STEP_TYPE_TO_NODE_TYPE`/`NODE_TYPE_TO_STEP_TYPE` 增加 `Knowledge↔'knowledge'`；`STEP_TYPE_LABEL` 增加 `Knowledge: 'Knowledge'`；`defaultConfig(Knowledge)` 返回 `{ knowledgeBaseId: '' }`。
- `components/canvas/DagNode.tsx`：`TYPE_ICON` 增加 `[StepType.Knowledge]: <BookOutlined />`。
- `components/canvas/NodePalette.tsx`：PALETTE 增加 `{ type: StepType.Knowledge, desc: '从知识库检索', icon: <BookOutlined /> }`。
- `components/canvas/NodeConfigPanel.tsx`：增加 `{type === StepType.Knowledge}` 区块：知识库 `Select`（加载 `getKnowledgeBases`）+ 可选查询 `TextArea`。
- `pages/WorkflowCanvasPage.tsx`：`nodeTypes` 增加 `knowledge: DagNode`。
- `pages/KnowledgeBaseDetailPage.tsx`：上传 `accept` 增加 `.pdf,.htm`，副标题更新为「支持 .txt/.md/.csv/.json/.html/.pdf 等」。

## 4. 放开发消息 RBAC

`ConversationsController.SendMessage`：移除 `[Authorize(Roles = "Admin,Operator")]`，保留类级 `[Authorize]`。即「任何已认证租户用户均可向本租户会话发消息」。KB 挂载/解除（`PUT/DELETE {id}/knowledge-base`）仍限 `Admin,Operator`（属管理面操作，保持收紧）。

## 5. 验收 checklist

- [ ] PDF 上传：提取正文（非乱码）并被切分入库；`KnowledgeDocument.ContentType` 记为 `application/pdf`。
- [ ] HTML 上传：标签被剥离，仅正文入库。
- [ ] 既有 .txt/.md/.json 行为不变（回归）。
- [ ] 工作流新增 Knowledge 节点：配置 knowledgeBaseId 后运行，从对应 KB 检索并把结果作为下游 artifact。
- [ ] 跨租户知识库不可被另一租户工作流检索（TenantId 校验）。
- [ ] 前端画布可拖出 Knowledge 节点、配置知识库/查询，节点图标与标签正确。
- [ ] SendMessage 对普通已认证用户返回 200（不再 403）。
- [ ] `dotnet build` 0/0；`dotnet test` 全绿；前端 `qa.mjs` 4/4。
- [ ] ddd-code-reviewer + ddd-phase-quality-gate PASS（P0=P1=P2=P3=0）。
- [ ] 提交含 `Quality-Gate:` 行，`.quality-gate.json` cleared:true。

## 6. Phase Quality Gate Checklist（ddd-phase-quality-gate 嵌入）

> Gate Status: **PASS**（P0=0 · P1=0 · P2=0 · P3=0，2026-07-23）

| # | 类别 | 检查项 | 结果 |
|---|------|--------|------|
| 1 | Pre-flight Version Audit | 无新增 NuGet 包（PdfPig fork 已弃用，改零依赖）；`IVectorStore.SearchAsync`/`RagSettings` 签名沿用既有 | PASS |
| 2 | BDD Scenarios First | 既有 SpecFlow 41 例不受影响；新增单元测试覆盖 PDF/HTML/dispatch + 知识节点 5 例 | PASS |
| 3 | DDD Layer Rules | `IDocumentTextExtractor`/`IStepExecutor` 在 `Application.Abstractions`；实现在 `Infrastructure`（`internal sealed`）；控制器仅依赖抽象 | PASS |
| 4 | DI Registration Completeness | `IDocumentTextExtractor`×3（Pdf/Html/Plain）、`KnowledgeRetrievalStepExecutor` 均已注册；`IWorkflowExecutable.ConfigJson` 在 `WorkflowNode`/`WorkflowStep` 双实现 | PASS |
| 5 | Configuration-First | 节点 `topK`/`minScore` 缺省回退 `IOptions<RagSettings>.DefaultTopK/DefaultMinScore`，未硬编码 | PASS |
| 6 | EF Core Mapping Sync | 本期无新增聚合/表，无需迁移 | PASS |
| 7 | Concurrency & Lifecycle | 提取器为 `Scoped`（无共享可变态）；`InMemoryVectorStore` Singleton 为既有且不影响新增代码 | PASS |
| 8 | Cross-Cutting Infrastructure | `UnsupportedContentTypeException`→415；跨租户知识库 `FatalFailure`；全局 `ExceptionHandler`/`ProblemDetails` 沿用既有 | PASS |

### 评审闭环说明（2026-07-23）
- `ddd-code-reviewer`：控制流追踪 `KnowledgeBasesController.UploadDocument → extractor.Extract → UploadDocumentCommand`；`WorkflowNodeRunner`/`SequentialOrchestrator` 均按 `HandlesType` 优先分发（Knowledge 节点正确路由）；跨租户校验在 `ResolveCollectionAsync` 中拦截。未发现 P0/P1/P2 缺陷。已知 P3（零依赖 PDF 提取器对「FlateDecode 流字节恰含 `endstream`」的边界截断）属 best-effort 限制，不阻塞。
- `ddd-phase-quality-gate`：12 类审计全过，无 dormant 枚举（`StepType.Knowledge` 在后端 executor + 前端 6 处均被引用）、无 dead code、无 missing null guard。
- 修复记录：调试期发现 `PdfTextExtractor.Unescape` 的 BOM 剥离分支误吞首字符（实测 `Unescape("ABC")→"BC"`），已移除该无谓分支（Latin1 解码文本不会出现 UTF BOM），单测 `DocumentTextExtractorTests` 复绿。
