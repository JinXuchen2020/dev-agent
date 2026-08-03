# F22 · 发布工作流为 API / MCP Server

> 状态：`doing`（§6 决策已于 2026-08-03 全部锁定 S1–S4，见下）。来源：F7 工作流平台化 program 子项 **④**。本文档为 feature-builder 取数单元；实现分支 `feat/f22-publish-api-mcp`（基于 origin/master，含 F20，未含 F21 触发器）。

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

## 6. 决策（✅ 2026-08-03 已锁定 S1–S4）
本 feature 为 🔴高风险（API Key 鉴权复用 + MCP 动态注册 + 外部输入隔离）。以下决策点**已由用户拍板锁定**，实现须严格遵循：
- **S1 API 鉴权复用（锁定）= 复用现有 `ApiKey` 中间件**：slug 端点 `POST /api/v1/published-workflows/{slug}` 直接走现有 `ApiKeyAuthenticationHandler`（F5 Phase 5 已建、`ApiKey` 聚合 + `IApiKeyRepository` + `TenantProvider` 同一套）。**确认该 scheme/handler 可作用于非 `/api-keys` 路由**（实现时验证；若默认仅绑定特定路由则需把 `ApiKeyAuthenticationHandler` 注册为通用认证 scheme 并在 slug 端点 `[Authorize]`）。绑定 key 复用 `ApiKeyId?` 字段指向既有 key；不新建鉴权体系。
- **S2 MCP 暴露形态（锁定）= 平台内 MCP tool 注册（v1）**：复用现有 `ToolDefinition`/MCP 暴露机制，把 `Enabled && Mode==Mcp` 的 `PublishedWorkflow` 纳入 `tools/list` + `tools/call`，**无独立进程/端口**。独立 MCP Server 形态划为 out（后续）。
- **S3 输入契约（锁定）= 用户自定义 `InputSchema`**：发布时在 `POST /workflows/{id}/publish` body 携带 `inputSchema`（JSON Schema 片段，存 `InputSchemaJson?`）；运行时按 schema 校验外部入参（失败 → 400）。不自动从 Start 节点推导。
- **S4 执行结果返回（锁定）= 仅最终输出（先打通）**：API/MCP 调用返回工作流运行的**最终输出**（与 `RunWorkflowCommand` 现有返回对齐），**不含中间节点 Trace**；Trace 返回留待 F24 之后再开，避免与 F24 执行 Trace 视图耦合。
- 其余默认：`PublishedWorkflow` 聚合（`ITenantScoped`，`Slug` 唯一索引 + 租户，`Id ValueGeneratedNever()`）；审计 `AuditActionType` 增 `PublishWorkflow`/`UnpublishWorkflow`；多租户隔离（发布绑定 TenantId，A 租户 slug 不触发 B 工作流，绑定 key 仅本租户有效）；**无破坏性既有聚合改动**（复用 `ApiKey`/`ToolDefinition`/`RunWorkflowCommand`）。

## Phase Quality Gate Checklist (F22 · 发布工作流为 API / MCP Server)

> 三道质量门 `ddd-code-reviewer` + `ddd-phase-quality-gate` + `codebase-optimizer` 已于实现后运行，结论 **0 open findings**，`cleared: true`。详细报告见 `docs/quality/f22-publish-api-mcp-gate.md`。

### 增量序列（模块级）
1. Domain 聚合 + 异常 + 枚举 + 仓储接口 → 编译 0 警告 → 单元（聚合构造/守卫）— 无独立测试（轻量）。
2. Application 五个 handler → 编译 0 警告 → 单元（`PublishWorkflowHandlersTests` 11 + `RunPublishedWorkflowCommandHandlerTests` 8）→ DI 审计 → 层审计。
3. Infrastructure 配置 + 仓储 + 迁移 → 编译 0 警告 → EF 映射审计 → 全方案测试。
4. Api 两控制器 + 异常处理器 → 编译 0 警告 → 单元/集成（`PublishedWorkflowsEndpointTests` 2，鉴权边界 401）→ 结构审计。
5. 前端发布 UI → `node scripts/qa.mjs` OVERALL PASS → i18n 对称测试。

### 12 类审计结果（G1–G12）
| 类别 | 结果 | 证据 |
|------|------|------|
| G1 DI 注册完整性 | PASS | `IPublishedWorkflowRepository`→`PublishedWorkflowRepository`（DependencyInjection.cs:105） |
| G2 DDD 层规则 | PASS | 接口在 Domain/Abstractions，实现 `internal sealed` 在 Infrastructure；Application 无 `using Infrastructure` |
| G3 EF Core 映射同步 | PASS | `PublishedWorkflowConfiguration` + DbSet + 迁移 `20260803035042` + 快照；`Id ValueGeneratedNever()` |
| G4 配置优先（无硬编码） | PASS | 无硬编码 GUID/密钥；slug 字母表为 const 局部 |
| G5 CancellationToken 透传 | PASS | 全部 handler/仓储透传 `ct` |
| G6 修饰符（internal sealed） | PASS | 仓储/配置/handler 均 `internal sealed` |
| G7 并发与生命周期 | PASS | 无新增 Singleton/可变共享；PerApiKey 框架内置限流 |
| G8 null 守卫 | PASS | 聚合构造 `ThrowIfNullOrWhiteSpace(slug)`；公共方法守卫齐 |
| G9 API 基础设施 | PASS | `PublishedWorkflowExceptionHandler` 注册（Program.cs:38）；控制器仅注入 `IMediator`+`ITenantProvider` |
| G10 蓝图漂移 | PASS（doc 措辞待同步） | 行为对齐 S1–S4；`IMcpToolProvider` 草拟名→落地 `McpController`（机制差异，Phase 6 同步） |
| G11 中文 XML 注释 | PASS | 新增公开类型/成员均带中文 `/// <summary>` |
| G12 Swagger/API 文档 | PASS | 沿用 Swashbuckle/Scalar；动作中文 XML 齐备 |

### 已知残留（非阻断，已记录）
- feature doc §2/§3 原草拟 `IMcpToolProvider` 命名与落地 `McpController` 不一致 → Phase 6 文档同步修订（仅措辞，不影响 S2 行为）。
- 控制器层 happy-path（带有效 API Key 的运行/列举）未做集成测试，由 handler 21 例单测 + 401 边界集成测试覆盖；后续可补 seed 化端到端。
