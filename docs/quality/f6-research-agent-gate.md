# F6 Research Agent 质量门报告

> Feature: `f6-research-agent`（联网多步调研）。分支：`feat/f6-research-agent`。
> 设计文档：`features/research-agent.md`。质量门契约见 `.quality-gate.json`（`cleared:true`）。

## 1. 范围与验收

| 维度 | 内容 |
|------|------|
| 后端 | `ISearchProvider` + `SerpApiSearchProvider`（真实 HTTP，非伪造）+ `ResearchCommand`/`ResearchCommandHandler`（plan→search×N→synthesize 多步链，SSE 事件流）+ `ResearchController`（`POST /api/v1/research`，SSE）+ `SearchSettings`（`IOptions`，密钥走环境变量不落库） |
| 前端 | `ResearchPage`（fetch + `credentials:'include'` 消费 SSE，实时 Timeline 进度 + 结构化报告渲染）、`types/index.ts` 类型、`api.ts runResearch`、`App.tsx` 路由、`AppLayout.tsx` 菜单 |
| 模型一致性 | 后端 camelCase 序列化；事件 `Type` 整型枚举（0–5），前端 `ResearchEventTypeValue` 常量对象一一对应；`tsc --noEmit` 0 错误 |
| 回归 | `dotnet build` 0 警告 0 错误；`dotnet test` 全方案 **238 测试全绿**；前端 `tsc --noEmit` + `vite build` 通过 |

## 2. ddd-code-reviewer（PASS，0 open）

**覆盖模块类型**：Multi-Agent/Orchestrator（Section C）+ API Controller（Section G）+ Configuration（Section H）+ Streaming/Resource Lifecycle（Section H2）+ General（Section Z）。

**已修复发现**：
1. **P3 — `SearchSettings.Provider` 未被消费**：原 DI 无条件注册 `SerpApiSearchProvider`，忽略 `Provider` 配置值 → 改为按 `Provider` 选择实现，未知值启动即 `throw InvalidOperationException`，消除"配置写了但不生效"的静默失败。
2. **P3 — 空问题返回 500**：`ResearchController` 原手动 `ThrowIfNullOrWhiteSpace` → ASP.NET 默认映射 500 → `ResearchRequest.Question` 加 `[Required]`，`[ApiController]` 自动返回 400。
3. **P1 — 死代码 `SearchResult.None` 单例**：全仓库零引用 → 删除。

**真实副作用核对（A/B 类）**：
- 搜索 HTTP 真实路径：`SerpApiSearchProviderTests`（StubHttpMessageHandler 模拟 SerpAPI）→ 断言真实 GET 构造（`engine=google`/`q`/`api_key`/`num`）+ `organic_results` 解析；缺 key / 502 / 超时 / 传输错误分别回打精准错误。
- 多步链：`ResearchCommandHandlerTests`（NSubstitute 脚本化 `IModelClient` 规划返回 JSON 查询数组、综合返回 Markdown + 脚本化 `ISearchProvider` 返回片段）→ 断言搜索调用 **N 次**（N=规划查询数）、`Sources` 按 URL 去重累积、`Sections`/`Answer` 非空、`StepsUsed`=N；计划/综合失败 → 精准 `Error` + 空 `Report`，绝不伪造。

## 3. ddd-phase-quality-gate（PASS，P0=P1=P2=P3=0）

**12 类审计结果**：

| 类别 | 结果 |
|------|------|
| DI 注册完整性 | `ISearchProvider` → `SerpApiSearchProvider`（Scoped）已注册 |
| DDD 分层 | 接口 `Application.Abstractions` / 实现 `Infrastructure` / DI `Infrastructure.DependencyInjection` |
| EF Core 映射 | 无新增聚合/VO，无需迁移 |
| 硬编码值 | 仅 SerpAPI 请求参数（`engine`/`num`），均可配（BaseUrl/TimeoutSeconds/DefaultMaxResults/SerpApiKey） |
| CancellationToken | `ChatAsync`/`SearchAsync`/`mediator.Send`/`WithCancellation` 全链路透传 |
| 修饰符 | `SerpApiSearchProvider`/`ResearchCommandHandler` `internal sealed`；`SearchSettings` `public sealed` |
| 并发/生命周期 | 无 Singleton 增长型集合；SSE 流由 ASP.NET 托管，客户端断开 → `OperationCanceledException` 已优雅收尾 |
| 空值守卫 | `Question` 经 `[Required]` + 处理器内 `ThrowIfNullOrWhiteSpace` |
| API 基础设施 | `[ApiController]` 自动校验；CORS 按全局策略（DEFERRED，非本轮范围） |
| 配置先行 | `SearchSettings` 经 `IOptions<T>`，密钥不写死 |
| 死代码/枚举常量 | 删除 `SearchResult.None` 后，新增枚举（`ResearchEventType`）/常量/清理 API 无零引用 |
| XML 文档 | 新增公共类型/成员均含中文 `/// <summary>` 及 `<param>` |
| Swagger | 沿用既有全局 SwaggerGen + XML 注释（控制器动作已含 `/// <summary>`） |

Checklist 已嵌入 `features/research-agent.md` §6。

## 4. codebase-optimizer（PASS，0 open）

七维度扫描 F6 模块结果：

- **架构**：`ResearchCommandHandler` 独立编排层，不污染工作流 step 语义；接口/实现/DI 分层清晰。
- **代码质量**：前端 0 `any`、TS `strict` 通过；后端实现类 `internal sealed` + 中文 XML 注释齐备。
- **正确性**：搜索真实 HTTP、LLM 经注入 `IModelClient`（生产为真实 SemanticKernel，测试为 stub）。
- **测试**：新增 8 例单测（Application 3 + Infrastructure 5），覆盖成功/失败/超时/缺 key/去重/预算。
- **性能**：`HttpClient` 走 `IHttpClientFactory` 池化；单搜索请求级 `CancellationTokenSource` 链接调用方 `ct`，不泄漏。
- **安全**：搜索密钥仅经 `SearchSettings`/环境变量 `Search__SerpApiKey`，**不落库**（不复用 `ToolDefinition.EndpointUrl`）。
- **工程化**：`dotnet build` 0 警告 0 错误；`tsc --noEmit` 0 错误；`vite build` 通过；前端无死代码。

按 feature-builder 约束，本优化在 `feat/f6-research-agent` 分支内分析+修复，**未新建 `codebase-optimizer/*` 分支、未推送**（与 feature-builder「不 push」硬约束一致）。

## 5. 已知残留

- `SerpApiKey` 为空时，各查询会返回失败，但报告仍基于已规划查询内容生成（优雅降级，非缺陷）。
- 真实 SerpApi 端到端需生产密钥；单测用 `StubHttpMessageHandler` 覆盖真实 HTTP 路径。
- 报告正文体为 Markdown 文本，前端以 `white-space: pre-wrap` 渲染（未引入 `react-markdown` 依赖；结构化字段 `Sources`/`Answer`/`Sections` 已拆分渲染）。

## 6. 门禁结论

```
Gate Status: PASS
[ P0: 0 | P1: 0 | P2: 0 | P3: 0 ]
cleared: true
codebaseOptimizer: PASSED
```
