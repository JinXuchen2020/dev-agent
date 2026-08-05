# F27 质量报告 · BDD 集成测试统一（Reqnroll + 文件 SQLite + Playwright E2E）

> 分支：`feat/bdd-integration` ｜ 日期：2026-08-04 ｜ 关联设计：`features/bdd-integration-design.md`

## 1. 验收闸门（端到端真实运行）

顶层编排 `node scripts/integration.mjs --e2e` 两次运行均绿：

| 阶段 | 验证 | 结果 |
|---|---|---|
| 阶段 1 · 后端 BDD | `dotnet test src/AgentPlatform.SpecFlowTests`（Reqnroll + 文件 SQLite `test-integration.db`，真 HTTP 走完整管线） | **51 / 51 passed** |
| 阶段 2 · 前端 E2E | `npx playwright test publish-workflow`（真实 Edge + Vite + Integration 后端 + 文件 SQLite `integration-e2e.db`） | **1 / 1 passed** |
| 卸载 | SIGTERM 后端 + 退避清理 `integration-e2e.db` | ✅ |

CI（`ci.yml` `integration` job）覆盖后端 BDD（跨平台无 Docker）。前端 E2E 为 Windows 本机闸门（`channel:'msedge'`）。

## 2. 三道质量门（对 F27 增量）

### 2.1 ddd-code-reviewer — PASSED（0 open）

对抗式审查聚焦生产面，发现并修复：

| 严重度 | 文件 | 发现 | 修复 |
|---|---|---|---|
| P2/P3 | `InfrastructureConfiguration.cs` + `scripts/integration.mjs` | 注释称「Integration 环境关闭限流」，但代码实际由 `Security:RateLimitingEnabled`（默认 true）开关控制，且 `startBackend` 未置 false——E2E 仅靠低调用量侥幸通过，未来增 spec 会触发 429 | `integration.mjs` 经 env `Security__RateLimitingEnabled=false` 真实关闭；注释修正为三路一致（env + 进程内 RemoveRateLimitPolicies + 开关） |

其余生产面（PublishMode 枚举修复、ExecutionLogRepository owned 实体修复、DatabaseInitializer 集成夹具幂等、ApiKeyRepository 跨租户查询）经审查均无缺陷。

### 2.2 ddd-phase-quality-gate — PASS（P0–P3 = 0 open）

12 类全扫结论：
- DI 注册完整（F27 无新增接口，夹具复用既有服务）
- DDD 层位正确（PublishMode→Domain；InfrastructureConfiguration→Api；Repository→Infrastructure `internal sealed`）
- EF 映射同步（F27 无新增聚合 → 无新增 `IEntityTypeConfiguration`）
- CT 透传（SeedIntegrationFixturesAsync / QueryStepsAsync 均带 `CancellationToken`）
- 并发与生命周期（无新增 Singleton / grow-only 集合；`integration.mjs` 为一次性脚本非常驻）
- 横切基础设施（CORS/Health/ExceptionHandler/ProblemDetails 未动）
- 蓝图漂移（无）、XML 文档、Swagger/OpenAPI、死代码（新增夹具常量均被引用）

结构门 checklist 已嵌入 `features/bdd-integration-design.md` §14。

### 2.3 codebase-optimizer — PASSED（七维 0 open）

架构 / 代码质量 / 正确性 / 测试 / 性能 / 安全 / 工程化全 PASS。无 `NotImplementedException`/`TODO`/`placeholder`/`stub`、无 `dangerouslySetInnerHTML`。后端 `dotnet test` 51/51、前端 `qa.mjs` OVERALL PASS、Playwright e2e 1/1。

## 3. E2E 真实捕获的生产 Bug（诚实记录）

`PublishMode` 整型枚举（`Api=0`）全 API 未注册 `JsonStringEnumConverter`，而前端发字符串 `"mode":"Api"` → ASP.NET 反序列化失败 **400**。
修复：`PublishMode` 标注 `[JsonConverter(typeof(JsonStringEnumConverter))]`（仅影响本枚举 JSON 序列化，不动全局整数枚举如 `WorkflowState`；不影响 C# 单测与 SpecFlow——后者序列化 `PublishMode.Api` 后变 `"Api"` 字符串，后端同样接受）。最小爆炸半径，后端 BDD 51/51 与前端口 QA 不受影响。

## 4. 已知残留 / follow-up（不阻塞 F27）

- 预存 e2e（create-agent / page-polish 等）断言**英文** UI 文本，但默认 locale=zh-CN（i18n F15），属与 F27 无关的预存语言环境错配，已使顶层闸门 E2E 收窄到 `publish-workflow` spec；其余预存 e2e 需各自修复。
- 玩具 `WorkflowStateMachine.feature` / `MultiAgentPipeline.feature` 测死接口（`[Obsolete]` 的 `IStateMachineEngine`/`IAgentOrchestrator`），零真实覆盖；建议 F27 收尾后删除（真实编排已由 `WorkflowEngine.feature` 经 `IOrchestrationPrimitive` 验证）。
- `GET /api/v1/api-keys` 后端未实现对应 controller（前端 ApiKeysPage 未完工特性），smoke 与 `publish-workflow` spec 均显式排除其 404。
