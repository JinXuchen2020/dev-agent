# F22 · 发布工作流为 API / MCP Server

> 状态：`open`。来源：F7 工作流平台化 program 子项 **④**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其 API Key 鉴权复用方式与 MCP 暴露形态）。

## 0. 目标
把已构建的工作流一键「发布」为可外部调用的能力：① 一个受 API Key 鉴权的 HTTP 端点；②（可选）暴露为 MCP Server 的工具，供 MCP 客户端调用。复用现有 API Key 体系与 `ToolDefinition` 基础。

## 1. 范围
**in**：
- 工作流「发布」开关：生成公开 `publishSlug` + 绑定一个或多个 `ApiKey`（复用 F5 之前已有的 `ApiKey` 聚合与鉴权）。
- **API 模式**：`POST /api/v1/published-workflows/{slug}`（携带输入 → 运行工作流 → 返回最终输出），API Key 鉴权（`[Authorize]` + key 解析，复用现有 `ApiKey` 中间件）。
- **MCP 模式**：把已发布工作流注册为 MCP tool（复用 `ToolDefinition`/MCP 基础），对外暴露 `tools/list` + `tools/call`。
- 发布管理 UI：发布/取消/查看 slug/绑定 key/切换模式。
- 多租户隔离（发布绑定 TenantId）+ 审计（PublishWorkflow/UnpublishWorkflow）。

**out**：工作流级配额/限流（可后续，复用 F13 配额）、MCP Server 独立部署形态（v1 仅作为平台内 MCP tool 暴露）。

## 2. 接口契约草案（后端）
- `POST /api/v1/workflows/{id}/publish` body `{ mode: Api|Mcp, apiKeyId?, inputSchema? }` → 返回 `publishSlug`（Admin,Operator）。
- `DELETE /api/v1/workflows/{id}/publish` → 取消发布。
- `GET /api/v1/workflows/{id}/publish` → 发布状态。
- `POST /api/v1/published-workflows/{slug}` → 运行（API Key 鉴权）。
- MCP：`IMcpToolProvider` 把已发布 MCP 模式工作流纳入 `tools/list`/`tools/call`（复用现有 MCP 暴露机制）。

## 3. 数据模型与改动面
- **新增聚合** `PublishedWorkflow`（ITenantScoped）：`{ Id, WorkflowId, TenantId, Slug, Mode(Api|Mcp), ApiKeyId?, InputSchemaJson?, Enabled, CreatedAt }` + EF 迁移（`Id ValueGeneratedNever()`，`Slug` 唯一索引 + 租户）。
- `PublishedWorkflowsController`（slug 端点，API Key 鉴权，复用 `IApiKeyRepository`/`TenantProvider`）。
- MCP 集成：`IMcpToolProvider` 实现读取 enabled MCP 模式 `PublishedWorkflow`，把 `InputSchema` 映射为 MCP tool schema。
- 审计：`AuditActionType` 增 `PublishWorkflow/UnpublishWorkflow`。
- **无破坏性聚合改动**：复用既有 `ApiKey`/`ToolDefinition`。

## 4. 风险
- 🔴 高风险：API Key 鉴权链路复用（需确认现有中间件可作用于新 slug 端点）、MCP tool 动态注册、发布工作流的执行隔离（运行上下文来自外部输入，需校验）。
- 缓解：slug 端点复用既有 cookie/JWT + API Key 双通道；MCP 复用既有 `ToolDefinition` 读取路径，仅新增 provider 实现；外部输入经 `ValidateGraph`/schema 校验。

## 5. 验收标准草案
- 发布后 `POST /published-workflows/{slug}` 携有效 API Key → 工作流运行并返回输出；无效 key→401；错误 slug→404。
- MCP 模式下外部 MCP client `tools/list` 可见该工作流、`tools/call` 可触发。
- 取消发布后 slug 失效、MCP 列表移除。
- 多租户：A 租户 slug 不触发 B 租户工作流；绑定 key 仅本租户有效。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（待锁定）
- **S1** API 鉴权复用：复用现有 `ApiKey` 中间件（确认可作用于非 `/api-keys` 路由）vs 新建轻量 key 校验。
- **S2** MCP 暴露形态：平台内 MCP tool 注册（v1）vs 独立 MCP Server 进程/端口（后续）。
- **S3** 输入契约：`InputSchema` 由用户定义 vs 从工作流 Start 节点自动推导。
- **S4** 执行结果返回：仅最终输出 vs 含中间节点 Trace（影响与 F24 集成）。
