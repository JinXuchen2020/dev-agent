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

## 6. 决策（待锁定）
- **S1** 模板来源：仅平台内置种子（v1）vs 开放用户发布 UGC（后续）。
- **S2** 模板存储：独立 `WorkflowTemplate` 聚合（v1）vs 复用 `Workflow` + `IsTemplate` 标记。
- **S3** 克隆时 `AssignedAgentId` 目标租户缺失处理：置空 + 提示 vs 报错。
- **S4** 分类体系：硬编码枚举 vs 自由 tag。
