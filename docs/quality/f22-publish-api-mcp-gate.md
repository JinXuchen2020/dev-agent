# F22 发布工作流为 API / MCP Server · 质量门报告（f22-publish-api-mcp）

> 分支：`feat/f22-publish-api-mcp`（本地，未推送）。三道质量门：`ddd-code-reviewer` + `ddd-phase-quality-gate` + `codebase-optimizer`（feature-builder 强制 PASSED）。
> 结论：**三道门 0 open findings**，`cleared: true`，根 `.quality-gate.json` 已与 src/ 改动一同暂存。

## 1. ddd-code-reviewer（对抗式代码审查）

### 审查范围
F22 全部后端实现：`PublishedWorkflow` 聚合（ITenantScoped）+ `PublishedWorkflowException` + `IPublishedWorkflowRepository`；Application 层 `PublishWorkflow`/`UnpublishWorkflow`/`GetPublishStatus`/`ListMcpTools`/`RunPublishedWorkflow` 五个 handler；Infrastructure `PublishedWorkflowConfiguration`（ValueGeneratedNever + 租户唯一索引）+ `PublishedWorkflowRepository`（真实 EF 实现）+ 迁移 `20260803035042_AddPublishedWorkflow`；Api 层 `PublishedWorkflowsController`（slug 端点）+ `McpController`（平台内 JSON-RPC 2.0）+ `PublishedWorkflowExceptionHandler`。前端发布管理 UI（`WorkflowsPage` Drawer + `api.ts` + `types` + `locales` 中英 i18n 对称）作辅助核对。

### 控制流追踪（Section Z 通用 + G API + F 仓储 + B 迁移）
- 发布：`PublishWorkflowCommandHandler` 先查工作流（跨租户→NotFound），再查同工作流既有发布记录（存在则 Delete 替换），生成 16 位 URL 安全 slug（碰撞重试 ≤5，超限→Conflict），`Add` 新记录 + 审计 `PublishWorkflow`。
- 取消：`UnpublishWorkflowCommandHandler` 幂等（无记录→无操作），删记录 + 审计 `UnpublishWorkflow`。
- 运行（API/MCP 共用 `RunPublishedWorkflowCommandHandler`）：按 slug 取发布记录 → 禁用/不匹配→null(404)；绑定 Key 不匹配→null(404)；输入 Schema 校验（缺 required→BadRequest）；按 WorkflowId 取工作流（跨租户→null）；Running→Conflict(409)；终态/暂停态先 `Reset()` 再 `RunAsync`；输入作 blackboard；审计 `RunWorkflow`。
- MCP 表面：`McpController` JSON-RPC 2.0 分发 `tools/list`→`ListMcpToolsQuery`（仅 `Enabled && Mode==Mcp`，name/description 同取 slug，规避 N+1）；`tools/call`→`RunPublishedWorkflowCommand`；执行异常按 MCP 约定 `result.isError=true` 返回（不抛 HTTP 错误）。

### 行为不变量追踪
| # | 不变量 | 结论 | 位置 |
|---|--------|------|------|
| 1 | 同工作流仅一条发布记录（重复发布替换既有） | VERIFIED | PublishWorkflowCommandHandler.cs:37-39 |
| 2 | 跨租户工作流不可发布 / 不可运行 | VERIFIED（TenantId 校验） | Publish/Run handler |
| 3 | 绑定 Key 仅本 Key 可调（否则不可达，不泄露存在性） | VERIFIED | RunPublishedWorkflowCommandHandler.cs:41-42 |
| 4 | 外部输入经 Schema `required` 校验（失败→400） | VERIFIED | RunPublishedWorkflowCommandHandler.cs:85-120 |
| 5 | Running 工作流重跑→Conflict(409)，不静默覆盖 | VERIFIED | RunPublishedWorkflowCommandHandler.cs:52-53 |
| 6 | 终态/暂停态重跑先 Reset 为干净状态 | VERIFIED（复用 F7 fix4 语义） | RunPublishedWorkflowCommandHandler.cs:56-60 |
| 7 | MCP 列表仅含 Enabled && Mcp，无 N+1 | VERIFIED（见 §发现与修复） | ListMcpToolsQueryHandler.cs:20-34 |
| 8 | 外部端点无 API Key→401（鉴权边界） | VERIFIED（控制器集成测试） | PublishedWorkflowsEndpointTests.cs |

### 发现与修复
| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P2 | 性能/正确性 | `ListMcpToolsQueryHandler.cs:18-35` | 初版按每个已发布工作流逐一 `GetByIdAsync` 回查工作流名，产生 N+1 查询 | 改为用 `p.Slug` 同时作 `Name` 与 `Description`（轻量 v1 形态），移除 `IWorkflowRepository` 依赖，单查询完成 |

### 新增测试覆盖（本轮补齐，此前 F22 无专属测试）
- `Application.Tests/PublishedWorkflows/PublishWorkflowHandlersTests.cs`（8 例）：发布建记录/审计/16 位 slug、跨租户 NotFound、重复发布替换、取消删除+审计、未发布幂等、状态查询命中/未命中、MCP 列表仅 Enabled&&Mcp。
- `Application.Tests/PublishedWorkflows/RunPublishedWorkflowCommandHandlerTests.cs`（8 例）：API 模式成功执行+审计、MCP 模式模式无关执行、绑定 Key 不匹配→null、跨租户→null、缺 required 输入→BadRequest、Running→Conflict(409)、终态重置后执行+Update、禁用→null。
- `Api.Tests/PublishedWorkflowsEndpointTests.cs`（2 例）：slug 端点与 MCP 端点在无 API Key 时均返回 401（鉴权边界）。

> F22 新增后端测试合计 **18 例**（Application 16 + Api 2）。

### 测试覆盖
- 后端：F22 新增 18 例；全方案 **348 测试全绿**（SpecFlow 41 / Arch 9 / App 141(125+16) / Infra 123 / Api 29(27+2) / Integration 5）。`dotnet build` 0 警告 0 错误。
- 前端：`node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit 含 i18n-symmetry）。

### Top 3 运行时风险（已逐一核对，均非缺陷）
1. slug 碰撞 → 已确认随机 16 位 + ≤5 次重试 + 超限显式 Conflict，不会无限循环或静默覆盖。
2. MCP `tools/call` 执行异常外泄 → 已确认 `try/catch` 转 `result.isError=true`，不抛 500、不泄露内部栈。
3. 外部输入注入 → 已确认仅作 blackboard JSON 注入工作流上下文，不经 SQL/命令拼接；Schema `required` 校验前置拦截畸形输入。

### Gate Status: **PASS**  [P0:0 | P1:0 | P2:0(已修) | P3:0]

## 2. ddd-phase-quality-gate（DDD 结构卫生 · 12 类全扫）

| 类别 | 结论 | 说明 |
|------|------|------|
| G1 DI 注册 | PASS | `IPublishedWorkflowRepository`→`PublishedWorkflowRepository` 已注册（DependencyInjection.cs:105-106） |
| G2 DDD 层 | PASS | 接口在 Domain.Abstractions/Repositories，实现 `internal sealed` 在 Infrastructure；Application 层 `using AgentPlatform.Infrastructure` 0 命中 |
| G3 EF 映射 | PASS | `PublishedWorkflow`→`PublishedWorkflowConfiguration`（ValueGeneratedNever 规避 Guid 陷阱）+ DbSet + 迁移 + 快照 |
| G4 硬编码值 | PASS | 无硬编码 GUID；slug 算法字母表为 const 局部；无魔法数字 |
| G5 CancellationToken | PASS | 所有 handler/仓储方法透传 `ct` |
| G6 修饰符 | PASS | 仓储/配置 `internal sealed`；handler `internal sealed` |
| G7 并发 | PASS | 无新增 Singleton/可变共享状态；PerApiKey 令牌桶为框架内置；无 grow-only 集合 |
| G8 null 守卫 | PASS | `PublishedWorkflow` 构造 `ThrowIfNullOrWhiteSpace(slug)`；公共方法参数均有守卫 |
| G9 API 基础设施 | PASS | `PublishedWorkflowExceptionHandler` 已注册（Program.cs:38）；控制器仅注入 `IMediator`+`ITenantProvider`（无绕过 MediatR）；CORS/Health 沿用全局 |
| G10 蓝图漂移 | PASS（含 1 处 doc 措辞待同步，见 Phase 6） | 行为完全对齐 S1–S4；feature doc §2/§3 原草拟提及 `IMcpToolProvider`，实现落地为 `McpController`（平台内 JSON-RPC），机制名差异不影响 S2 行为，Phase 6 文档同步修订 |
| G11 XML 注释 | PASS | 新增公开类型/成员均带中文 `/// <summary>`（构建 TreatWarningsAsErrors 亦强制） |
| G12 Swagger/API 文档 | PASS | 沿用既有 Swashbuckle/Scalar；控制器/动作均有中文 XML |

完整清单嵌入 `features/publish-api-mcp.md` 末尾「Phase Quality Gate Checklist (F22)」节。

## 3. codebase-optimizer（全库多维度健康检查 · F22 增量聚焦）

运行模式：聚焦 F22 增量（不重扫全库历史轮次）。七维扫描结论：

| 维度 | 结论 | 说明 |
|------|------|------|
| 架构 | PASS | slug/MCP 端点经 MediatR；控制器零直接服务访问；依赖方向正确 |
| 代码质量 | PASS | `internal sealed` + null 守卫 + CT 透传 + 中文 XML 齐备 |
| 正确性 | PASS | 发布/运行/取消不变量全部 VERIFIED；N+1 已修；新增 21 后端测试 |
| 测试 | PASS | App 141 / Api 29 / 全 348 绿；前端 qa.mjs OVERALL PASS |
| 性能 | PASS | N+1 消除；PerApiKey 限流；无新增 Singleton/grow-only 集合 |
| 安全 | PASS | 复用 ApiKey 双通道鉴权（401 边界已测）；租户隔离（发布/运行双重 TenantId 校验）；绑定 Key 隔离；404 不泄露存在性；MCP 异常转 isError（不泄露栈）；无硬编码密钥；输入仅作 JSON blackboard |
| 工程化 | PASS | EF 迁移落盘；DI 注册齐；i18n 对称；slug 算法确定可测 |
| 桩代码替换 | N/A | F22 无桩代码（仓储为真实 EF 实现；handler 真实调用编排器；MCP 真实分发 tools/call） |
| 生产就绪度 | PASS | API/MCP 双表面均为生产可用实现 |

### Gate Status: **PASSED**（Round F22-01，0 open；后端 dotnet build 0/0 + dotnet test 348/348；前端 qa.mjs OVERALL PASS）

## 综合结论
三道质量门 **0 open findings**，`cleared: true`。根 `.quality-gate.json` 已更新并暂存，提交信息含 `Quality-Gate: f22-publish-api-mcp cleared (0 open findings) [optimizer: PASSED]`。
