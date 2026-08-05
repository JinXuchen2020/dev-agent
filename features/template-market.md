# F23 · 模板市场 / 示例库

> 状态：`open`。来源：F7 工作流平台化 program 子项 **⑤**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其模板来源：内置种子 vs 用户发布市场）。

## 0. 目标
提供 5–10 个行业/场景工作流模板（知识库问答、文档摘要、定时爬虫、多 Agent 评审、客服分流等），用户一键克隆为属于自己的工作流，降低上手成本。对标 Dify「模板」/ Coze「Bot 商店」。

## 1. 范围
**in**：
- 平台内置模板库（种子数据，随 `DatabaseInitializer` 落地，tenant-agnostic 或绑定平台租户）。
- 「模板中心」前端页：分类/搜索/预览（只读快照）/「一键克隆为我的工作流」。
- 克隆端点：`POST /api/v1/workflow-templates/{id}/clone` → 创建租户内新 `Workflow`（复用 F7 ① 的 `WorkflowGraphSnapshot`/`ReplaceGraph` 重建图），返回 `WorkflowDetail`。
- 多租户：模板平台级共享，克隆后归当前租户（隔离）。
- 审计（CloneTemplate）。

**out（v1）**：用户自建模板发布到市场（UGC）、模板版本、模板评分/收藏（后续 feature）。

## 2. 接口契约草案（后端）
- `GET /api/v1/workflow-templates?category=&keyword=` → 列表（任意已认证可读，平台级）。
- `GET /api/v1/workflow-templates/{id}` → 模板详情（含预览 nodes/edges）。
- `POST /api/v1/workflow-templates/{id}/clone` → 克隆为当前租户新工作流（Admin,Operator，与创建工作流同权）。
- 可选：`GET /api/v1/workflow-templates/categories` → 分类枚举。

## 3. 数据模型与改动面
- **新增聚合** `WorkflowTemplate`（可平台级，不强绑 TenantId，或 `TenantId = platform` 常量）：`{ Id, Name, Category, Description, SnapshotJson, Tags, CreatedAt }` + EF 迁移 + `DatabaseInitializer` 种子 5–10 条。
- `CloneWorkflowTemplateCommand/Handler`：读模板 `SnapshotJson` → `WorkflowGraphSnapshot.FromJson` → `ReplaceGraph` 建新 `Workflow`（新 Id/租户/名称 `+ " (副本)"`）。
- 前端 `TemplateMarketPage`：列表（卡片，复用 F16 `EntityCardGrid`）+ 预览抽屉 + 克隆按钮（RBAC）。

## 4. 风险
- 🟡 中风险：种子模板的图需通过 `ValidateGraph`（不能含非法图）；克隆后节点 `AssignedAgentId` 在目标租户可能不存在（需降级/提示）。
- 缓解：种子模板用平台内置 Agent 或 `AssignedAgentId=null`（运行时不绑定）；克隆校验 agent 存在性，缺失则置空并提示。

## 5. 验收标准草案
- 模板列表/详情正确，分类/搜索可用。
- 一键克隆生成新工作流，图结构与模板一致，归当前租户。
- 克隆后原模板不受改动影响（隔离）。
- 多租户：A 克隆不影响 B；模板平台级只读。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（已锁定 2026-08-05）
- **S1** 模板来源：**仅平台内置种子（v1）**。用户发布 UGC 明确排除在 v1 外（见范围 out）。种子随 `DatabaseInitializer` 幂等落地，平台级共享（所有租户可读）。
- **S2** 模板存储：**独立 `WorkflowTemplate` 聚合（v1）**，不污染 `Workflow` 表（避免 `IsTemplate` 标记带来的查询过滤器/编排器污染）。`WorkflowTemplate` **非 `ITenantScoped`**（平台级共享，对所有租户可见，不受租户查询过滤器约束），克隆后生成的新 `Workflow` 才带当前租户。
- **S3** 克隆时 `AssignedAgentId`：**置空 + 提示（降级）**。平台模板不绑定任何租户的 Agent（种子快照 `AssignedAgentId=null`），因此克隆出的工作流节点不预绑 Agent，用户在编辑/运行前自行指派。克隆处理器在映射 `ReplaceGraph` 入参时统一将 `AgentId` 置 `null`，彻底杜绝跨租户 Agent 引用泄漏。
- **S4** 分类体系：**硬编码枚举 `WorkflowTemplateCategory`**（General / KnowledgeQa / Summarization / WebScraping / MultiAgentReview / CustomerSupport / ContentGeneration / DataAnalysis）。新增 `GET /api/v1/workflow-templates/categories` 返回枚举值列表。
- **S5（派生）** 克隆鉴权：`[Authorize(Roles="Admin,Operator")]`（与创建工作流同权），创建租户内新 `Workflow`；列表/详情/分类端点任意已认证可读。
- **S6（派生）** 审计：克隆动作写 `AuditActionType.CloneTemplate`（entity=`Workflow`，tenantId=当前调用者租户，entityId=新建工作流 Id）。
- **S7（派生）** 无 EF 迁移风险以外的破坏性改动：仅新增聚合/表/端点，不改既有契约；`ValidateGraph` 在克隆 `ReplaceGraph` 时强制（种子快照均构造合法图：1 Start + ≥1 End + 无环 + 从 Start 连通 + 节点名唯一）。

## Phase Quality Gate Checklist（F23 质量门，2026-08-05 闭环）

> 闸门 = `ddd-phase-quality-gate`（审计）+ `ddd-code-reviewer`（对抗）+ `codebase-optimizer`（七维体检），结论 **PASS（P0/P1/P2/P3 = 0）**。详见 `docs/quality/f23-template-market-gate.md` 与 `.quality-gate.json`。

### 1. Pre-flight Version Audit
- [x] 无新增 NuGet 包（复用 Semantic Kernel / MediatR / EF Core 既有依赖）
- [x] `dotnet build` 0 警告 0 错误；前端 `qa.mjs` typecheck/lint/build/unit 全绿

### 2. BDD Scenarios First
- [x] 后端单测覆盖核心不变量（Clone 副本命名 / Agent 解绑 / 审计 / 缺失返回 null / List 透传筛选 / Get 解码图 / Categories 全量）；前端 e2e 见 §后续

### 3. DDD Layer Rules
- [x] `IWorkflowTemplateRepository` 接口在 `Domain.Repositories`；实现 `WorkflowTemplateRepository` 在 `Infrastructure.Persistence.Repositories`；DI 注册在 `Infrastructure/DependencyInjection.cs`（Scoped）
- [x] Application 层不引用 Infrastructure（已 grep 确认 0 处）
- [x] `WorkflowTemplate` 为平台级聚合，**刻意不实现 `ITenantScoped`**（S2），克隆产物 `Workflow` 才带租户

### 4. DI Registration Completeness
- [x] `IWorkflowTemplateRepository → WorkflowTemplateRepository`（Scoped）已注册，可被 `IServiceProvider` 解析

### 5. Configuration-First
- [x] 无新增魔法数 / 硬编码模型名；种子固定 Guid 为幂等播种约定（同 ApiKeys/Workflows），非配置值

### 6. EF Core Mapping Sync
- [x] `WorkflowTemplateConfiguration : IEntityTypeConfiguration<WorkflowTemplate>` 已建；`Id.ValueGeneratedNever()` 规避 Guid 主键 UPDATE 命中 0 行陷阱
- [x] 迁移 `20260805043045_AddWorkflowTemplate` 已生成并含 `#pragma warning disable IDE0161`

### 7. Concurrency & Lifecycle
- [x] 无新增 Singleton / grow-only 集合；`WorkflowTemplateRepository` 为 Scoped，无跨请求可变状态
- [x] 种子签名图均合法（1 Start + ≥1 End + 无环 + 从 Start 连通 + 节点名唯一）→ 克隆 `ValidateGraph` 不会 500

### 8. Cross-Cutting Infrastructure
- [x] `WorkflowTemplatesController` 仅注入 `IMediator` + `ITenantProvider`（合规）；列表/详情/分类任意已认证可读，克隆 `[Authorize(Roles="Admin,Operator")]`
- [x] 全部 async 方法透传 `CancellationToken`；实现类 `internal sealed`；公共类型/成员含中文 `/// <summary>`
- [x] 克隆处理器对 `template == null` → 返回 null（控制器转 404），审计 `CloneTemplate` 落库

### 对抗式审查（ddd-code-reviewer）修复项
- [x] **P1 健壮性**：前端 `getWorkflowTemplates` 原将 `keyword: null` 直接传入 axios params，可能序列化为 `keyword=null` 导致后端误过滤为空列表；改为仅含非 null 键的条件 params（已修复 + tsc 验证）
- [x] **P2 测试覆盖**：新增 `List_PassesCategoryAndKeywordToRepository` 验证查询契约透传

### 七维体检（codebase-optimizer，F23 范围）
- [x] 架构 / 代码质量 / 正确性 / 测试 / 性能 / 安全 / 工程化 —— F23 增量 **0 open**（详见质量门报告）
