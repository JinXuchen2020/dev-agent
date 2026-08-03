# BDD 集成测试统一设计（最终集成层 = 真 HTTP + 真 DB + 前端 E2E）

> 状态：设计稿（2026-08-03）  
> 决策已锁定：Reqnroll + 文件 SQLite + Playwright + 现有 41 例 SpecFlow 全量迁移到 HTTP+DB  
> 关联：F22（发布工作流为 API/MCP Server）、`features/publish-api-mcp.md`、`docs/quality/f22-publish-api-mcp-gate.md`

---

## 1. 目标与原则

把 **BDD 重新定义为平台的最终集成测试层**，统一契约：

1. **真 HTTP** —— 不打 handler / 不构造假 Repository，全部经 `HttpClient` 走真实管线（认证中间件、限流、异常处理器、MediatR+UoW、EF）。
2. **真 DB** —— 不用 in-memory SQLite（Api.Tests 现行做法**不属此层**），用独立磁盘文件 SQLite（每次运行全新文件，仍走真实 schema 迁移 + 磁盘 I/O）。
3. **前端 E2E 并入本层** —— 真实浏览器驱动 Vite App → 后端 HTTP → 真 DB，覆盖 UI 全链路。
4. **全量统一** —— 现有 41 例 SpecFlow 域级测试（假对象）**全部迁移**到本契约；新 BDD 天然遵守。

原则：**BDD = 验收级集成测试**，位于测试金字塔顶层；单元层（xUnit + NSubstitute mock）保留在底层不改动。

---

## 2. 测试分层（改造后）

| 层 | 技术 | 依赖 | 范围 | 现状 |
|---|---|---|---|---|
| 单元层 | xUnit + NSubstitute | mock 依赖 | handler / 域逻辑 | 保留（Application.Tests / Infra.Tests 等） |
| HTTP 集成层（非 BDD） | xUnit + WebApplicationFactory | in-memory SQLite | 端点/中间件契约（如 401 边界） | 保留，但**不计入 BDD 层** |
| **BDD 集成层（最终）** | **Reqnroll + WebApplicationFactory** | **真 HTTP + 文件 SQLite** | **端到端行为验收** | **本设计新建/迁移目标** |
| **前端 E2E 层** | **Playwright (@playwright/test)** | **真浏览器 + Vite + 后端 HTTP + 文件 SQLite** | **UI 全链路验收** | **本设计新建** |

---

## 3. 技术选型（已与用户锁定）

| 项 | 选择 | 理由 |
|---|---|---|
| BDD 框架 | **Reqnroll 3.x**（`Reqnroll` + `Reqnroll.xUnit` + `Reqnroll.Tools.MsBuildGeneration`） | SpecFlow 商业授权收紧后停止主版本；Reqnroll 是开源继任者，Gherkin/绑定语法近 100% 兼容，迁移成本极低 |
| 集成 DB | **文件 SQLite**（每次运行独立 `test-integration.db`） | 零基础设施依赖、CI 友好，仍具真实磁盘 I/O 与迁移；与生产 dev SQLite 一致 |
| 前端 E2E | **Playwright（`@playwright/test`）** | TS 原生、Vite/React 支持好、可同时驱动后端、报告/追踪强 |
| 旧 SpecFlow | **全部迁移到 HTTP+DB** | 全仓 BDD 契约统一（纯域内部行为见 §7 例外处置） |

---

## 4. 后端 BDD 集成设计

### 4.1 工程改造（SpecFlow → Reqnroll）
- `AgentPlatform.SpecFlowTests.csproj`：移除 `SpecFlow` / `SpecFlow.xUnit`，改引 `Reqnroll` / `Reqnroll.xUnit` / `Reqnroll.Tools.MsBuildGeneration`。
- 所有 `.feature` 文件**保留不动**（Gherkin 语法兼容）。
- 所有 Steps 文件 `using TechTalk.SpecFlow;` → `using Reqnroll;`；`[Binding]`/`[Given/When/Then("regex")]` 属性名不变。
- 删除旧的 `.feature.cs`（由 Reqnroll 代码生成器重新产出）。
- 重命名工程为 `AgentPlatform.BddTests`（可选，保持语义清晰），同步 `.sln`。

### 4.2 测试基座（核心）
新增 `IntegrationAppFactory : WebApplicationFactory<Program>`：

```csharp
public sealed class IntegrationAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DbPath = "test-integration.db";
    public HttpClient Api { get; private set; } = null!;
    public string ApiKey { get; } = "test-integration-key";   // 种子明文
    public string Jwt { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Integration");
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string>(
                    "ConnectionStrings:DefaultConnection",
                    $"Data Source={DbPath}")
            }));
        // 关闭 PerApiKey 限流以稳定测试（或注入测试 key 白名单）
        builder.ConfigureServices(s => s.RemoveRateLimitPolicies());
    }

    public async Task InitializeAsync()
    {
        if (File.Exists(DbPath)) File.Delete(DbPath);
        Api = CreateClient();
        await IntegrationSeeder.SeedAsync(Services, ApiKey); // 建库+迁移+种子
        Jwt = await AuthHelper.LoginAsTenantUserAsync(Api);  // 种子用户 JWT
    }
    public new Task DisposeAsync() { if (File.Exists(DbPath)) File.Delete(DbPath); return Task.CompletedTask; }
}
```

- **环境 `Integration`**：扩展 `Program.cs:58` 的 `IsDevelopment()||IsEnvironment("QuickStart")` → 增 `||IsEnvironment("Integration")`，使 `DatabaseInitializer` 在该环境跑迁移 + 基础种子（角色/agent 配置）。
- **种子 `IntegrationSeeder`**：在基础种子之上插入**专用集成租户**（`integration-tenant`）、用户（已知凭据）、**已知明文 ApiKey**（`test-integration-key`，经 `IApiKeyEncryptionService` 加密落库）、以及一个**已完成状态（Completed）的示例工作流**（供发布场景使用）。
- **认证 helper**：
  - 发布类端点（JWT）：`AuthHelper.LoginAsTenantUserAsync` 用种子用户登录拿 JWT，Step 中加 `Authorization: Bearer <jwt>`。
  - 运行类端点（ApiKey）：Step 中直接加 `X-Api-Key: test-integration-key`。

### 4.3 F22 场景目录（Gherkin，真 HTTP）
`Features/PublishedWorkflow.feature`（路由已核实）：

```gherkin
Feature: 发布工作流为 API / MCP Server（F22，真 HTTP + 真 DB）
  Background:
    Given 集成租户下存在一个 Completed 状态的工作流 W1
    And 集成租户持有一个有效的 ApiKey "test-integration-key"

  Scenario: 发布为 API 模式并生成 slug
    When 对 W1 发送 POST /api/v1/workflows/{w1Id}/publish
    Then 响应 200 且返回 16 位 URL 安全 slug
    And GET /api/v1/workflows/{w1Id}/publish 状态为 Enabled

  Scenario: 用绑定 Key 经 slug 运行
    Given W1 已发布为 Api 模式并绑定该 Key
    When 带 X-Api-Key 发送 POST /api/v1/published-workflows/{slug} 并附必填输入
    Then 响应 200 且返回工作流最终输出

  Scenario: 错误 Key 被拒（不泄露存在性）
    Given W1 已发布并绑定该 Key
    When 用其他租户的 Key 发送 POST /api/v1/published-workflows/{slug}
    Then 响应 404

  Scenario: 跨租户不可运行他人发布
    Given 另一租户发布了 W2
    When 集成租户用自身 Key 调用 W2 的 slug
    Then 响应 404

  Scenario: MCP tools/list 仅暴露启用且 Mcp 模式
    Given W1 发布为 Mcp 模式并启用
    And 存在一条 Api 模式的发布记录
    When 带 X-Api-Key 发送 POST /api/v1/mcp (tools/list)
    Then tools 列表仅含 W1

  Scenario: 取消发布后 slug 不可用
    Given W1 已发布
    When 发送 DELETE /api/v1/workflows/{w1Id}/publish
    Then 再调用 slug 端点返回 404
```

> 注：以上为 BDD 验收场景；F22 已有的 18 例 xUnit（handler + 401 边界）保留在单元/HTTP 集成层，不与本层重复断言。

### 4.4 迁移现有 41 例到 HTTP+DB
见 §7。

---

## 5. 前端 E2E 设计（Playwright）

### 5.1 目录与配置
- 新增 `src/AgentPlatform.Web/e2e/`：`playwright.config.ts` + `*.spec.ts`。
- `package.json` 加 `"e2e": "playwright test"`、`"e2e:ui": "playwright test --ui"`，devDependency `@playwright/test`。
- `playwright.config.ts`：
  - `webServer`：启动 Vite dev（`npm run dev`，端口 5173），`reuseExistingServer: true`。
  - `baseURL: http://localhost:5173`。
  - 后端依赖：由顶层编排脚本先起 `IntegrationAppFactory` 对应服务（见 §6），E2E 通过 `API_BASE` 指向 `http://localhost:5000`（或 Playwright `request` fixture 直连）。
  - `use: { trace: 'on-first-retry', screenshot: 'only-on-failure' }`。

### 5.2 场景（F22 全链路 UI）
`e2e/publish-workflow.spec.ts`：

```gherkin
# 用 Playwright 的 test() 描述，等价于：
Scenario: 在 UI 发布工作流并调用
  Given 后端已起（Integration 环境 + 文件 SQLite + 种子）
  When 打开 Workflows 页并登录集成租户用户
  And 打开某 Completed 工作流的发布 Drawer，选择 Api 模式，点击发布
  Then Drawer 显示 slug 与调用端点
  When 复制 slug 并用 ApiKey 经端点调用
  Then 页面/接口返回工作流最终输出
```

- 复用后端同一套种子（集成租户 + ApiKey + 示例工作流），保证前后端 E2E 数据一致。
- 登录态：通过注入种子用户 JWT（localStorage/session）或走登录页。

---

## 6. 编排与 CI

顶层脚本 `scripts/integration.(sh|mjs)`（或 `Makefile` target `make integration`）：

1. 启动后端集成服务：`dotnet run --project src/AgentPlatform.Api --launch-profile Integration`（或 `dotnet test` 内 WebApplicationFactory 自管，无需常驻）。
2. 启动前端：`cd src/AgentPlatform.Web && npm run dev`（Playwright `webServer` 自动管）。
3. 跑后端 BDD：`dotnet test src/AgentPlatform.BddTests`（Reqnroll，HTTP+文件 SQLite）。
4. 跑前端 E2E：`npm run e2e`。
5. 卸载：杀后端 / 删 `test-integration.db`。

**CI（deploy/*.yml 增 job `integration`）**：
- 后端 BDD 与前端 E2E 作为**合并前最终闸门**（接在现有 `qa.mjs` + 单测之后）。
- 产物：Reqnroll HTML 报告 + Playwright HTML 报告 + trace。
- 质量门（`.quality-gate.json`）新增 `bdd: PASSED` 字段，未过不得合入。

---

## 7. 现有 41 例迁移计划（含例外处置）

| 现有 feature | HTTP 映射 | 处置 |
|---|---|---|
| AgentRouting | `POST /api/v1/agents` + `POST /api/v1/conversations/{id}/messages` 路由断言 | 迁移 |
| AgentTypeMigration | agent role code 迁移经 agent CRUD | 迁移 |
| CustomAgentRole | `POST /api/v1/agent-roles` | 迁移 |
| ExecutionLog | `GET /api/v1/execution-logs` | 迁移 |
| MultiAgentPipeline | conversation 多 agent 消息管线 | 迁移 |
| WorkflowStateMachine | 状态机**重试/回滚内部**——无公开 HTTP 表面 | **例外**（见下） |

**WorkflowStateMachine 例外处置（诚实工程判断）**：
重试/回滚是 Workflow 执行引擎的纯算法行为，经 HTTP 仅能触达「启动工作流→观察终态」，无法精细断言「第 2 步重试 3 次后标记 Failed」「回滚已完成步骤」。两种出路：
- **(A)** 新增受控测试端点（如 `POST /api/v1/workflows/{id}/debug-run` 暴露逐步事件）——为测试加生产端点，**不推荐**。
- **(B 推荐)** 该 feature 以「**域集成**」方式迁移：仍连**真文件 SQLite DB**，但通过应用层命令（`IMediator.Send(new StartWorkflowCommand{...})`）驱动并断言终态/回滚记录落库，**不走公开 HTTP**。既遵守「真 DB」契约，又不为测试污染生产路由。

> 若用户坚持「绝对全部 HTTP」，则对 WorkflowStateMachine 采用 (A) 并明确标注该端点为 `[Authorize(Roles=admin)]` 仅测试/运维可用。默认按 (B)。

---

## 8. 目录结构（目标态）

```
src/
  AgentPlatform.BddTests/            # 原 SpecFlowTests 改名
    Features/
      AgentRouting.feature
      AgentTypeMigration.feature
      CustomAgentRole.feature
      ExecutionLog.feature
      MultiAgentPipeline.feature
      WorkflowStateMachine.feature   # 域集成（B 处置）
      PublishedWorkflow.feature       # 新增 F22
      *.feature.cs                    # Reqnroll 生成
    Steps/                           # 绑定（HTTP 客户端驱动）
    IntegrationAppFactory.cs         # WebApplicationFactory + 文件 SQLite
    IntegrationSeeder.cs             # 集成租户/用户/ApiKey/示例工作流种子
    AuthHelper.cs                    # JWT / ApiKey 注入
  AgentPlatform.Web/
    e2e/
      playwright.config.ts
      publish-workflow.spec.ts
      fixtures/                      # 种子数据常量
scripts/
  integration.(sh|mjs)               # 顶层编排
deploy/
  *.yml                              # 增 integration job
```

---

## 9. 分阶段实施

| 阶段 | 内容 | 验收 |
|---|---|---|
| **A. 基座** | SpecFlow→Reqnroll；`IntegrationAppFactory` + 文件 SQLite + `Integration` 环境 + `IntegrationSeeder` + `AuthHelper` | 空 BDD 工程能起服务、能连真 DB、能拿 JWT/ApiKey |
| **B. 迁移** | 5 个可达 feature 迁 HTTP+DB；WorkflowStateMachine 走域集成(B) | 41 例全绿（Reqnroll 报告） |
| **C. F22 BDD** | 写 `PublishedWorkflow.feature` 6 场景 + Steps | F22 行为经真 HTTP+DB 全绿 |
| **D. 前端 E2E** | Playwright 配置 + `publish-workflow.spec.ts` + 种子对齐 | UI 全链路绿 |
| **E. 编排/CI** | `scripts/integration` + `deploy/*.yml` integration job + 质量门 `bdd` 字段 | CI 合并前闸门通过 |

---

## 10. 验收标准（整体）

- ✅ 所有 BDD 场景经**真 HTTP + 文件 SQLite** 运行，零 mock Repository、零 in-memory。
- ✅ 现有 41 例 + F22 新场景全绿（Reqnroll HTML 报告）。
- ✅ Playwright E2E 覆盖 F22 前端全链路，绿（HTML 报告 + trace）。
- ✅ `scripts/integration` 一键编排后端 BDD + 前端 E2E + 卸载。
- ✅ CI `integration` job 作为合并前最终闸门；`.quality-gate.json` 增 `bdd: PASSED`。
- ✅ Api.Tests 的 in-memory SQLite 用法明确**不计入 BDD 层**（保留作轻量 HTTP 契约测）。

---

## 11. 风险与开放问题

1. **WorkflowStateMachine 内部行为不可经 HTTP 触达** → 默认域集成(B)，待实现确认（§7）。
2. **限流（`PerApiKey`）干扰 BDD** → 基座中移除/白名单测试 key，或在 `Integration` 环境关闭。
3. **种子 ApiKey 明文落库需经 `IApiKeyEncryptionService`** → 不能直写明文列，须走加密服务（沿用 F13 既有加密基件）。
4. **前端 E2E 与后端种子一致性** → 前后端共用同一 `IntegrationSeeder` 常量（tenant/key/workflow id），避免漂移。
5. **运行时长** → 真 DB + 浏览器比 unit 慢；CI 并行 + trace 仅失败保留以控成本。
6. **Reqnroll 与现有 `.feature.cs` 冲突** → 迁移前删旧生成文件，交由 Reqnroll 重新生成。

---

## 12. 下一步

1. 用户确认本设计（含 §7 例外处置默认 B）。
2. 在 `features/backlog.md` 登记新 initiative「BDD 集成测试统一（Reqnroll + 文件 SQLite + Playwright E2E）」。
3. 进入阶段 A 实施（按 feature-builder 流程，建 `feat/bdd-integration` 分支 + 质量门）。
