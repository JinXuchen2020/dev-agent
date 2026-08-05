# F28 质量门报告 — 历史 feature BDD 测试覆盖补全

> 阶段：F28（B1–B8 后端 Reqnroll BDD + 前端 playwright-bdd E2E 全量覆盖）
> 分支：`feat/f28-bdd-coverage`（不 push）
> 日期：2026-08-05

## 总览

| 门 | Skill | 结果 | 增量 open findings |
|----|-------|------|--------------------|
| 1 | ddd-code-reviewer | PASS（修复 1 个 P1 后清零） | 0 |
| 2 | ddd-phase-quality-gate | PASS（结构化审计 12 类 0 open） | 0 |
| 3 | codebase-optimizer | PASS（七维度分析 F28 增量 0 open，未建分支/不 push 以遵守 feature-builder 硬约束） | 0 |

## 验证基线（闸门实测）

- 后端 BDD：`dotnet test src/AgentPlatform.SpecFlowTests` → **114/114**（0 fail）。
- 前端 E2E：`node scripts/integration.mjs --e2e` → 后端 BDD 114 + 前端 BDD **22/22**（@e2e，Edge 单 worker）。
- 单元测试回归：`dotnet test src/AgentPlatform.Infrastructure.Tests` → **124/124**（含新增 Stub 守卫测试）。

## 门 1 · ddd-code-reviewer

对抗式审查聚焦 F28 唯一触达生产代码的改动 `TenantModelClientResolver.cs`（Stub 短路守卫），并核查测试基础设施与 E2E 选择器。

### 发现与修复

| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P1 | 编译中断 / 测试覆盖缺口 | `TenantModelClientResolverTests.cs` | 生产构造函数新增 `IConfiguration` 参数，但现有 3 个单测的 `Create` 辅助仍传 4 参 → `AgentPlatform.Infrastructure.Tests` 无法编译；且新 Stub 分支无测试覆盖。 | `Create` 增可选 `IConfiguration` 参数（默认空配置兜底，现有 3 测不变）；新增 `ResolveAsync_ReturnsEmpty_WhenProviderIsStub_WithoutResolvingCredentials`，断言 Stub 模式短路时**绝不**调用凭据解析/解密（防真实 LLM 网络请求）。 |

### 控制流 / 回退核对

- `ModelRouter.RouteAsync`（line 59, 77-79, 94-95）：`tenantResolutions` 为空时回退到 `_platformModelClient`（平台 stub 客户端）→ Stub 模式返回空列表不崩溃，E2E 会话收到 stub 回复已验证。
- `PlatformModelsController`（line 42）：空解析仅返回空模型列表，无副作用。
- DI：`IConfiguration` 为框架内置根服务，自动解析，无需新增注册。

### Top 3 运行时风险（已确认非问题）

1. Stub 短路致 `ResolveAsync` 返回空 → 由 `ModelRouter` 回退平台客户端覆盖；E2E 已实证。（非风险）
2. `TenantModelClientResolver` 在 Stub 下不解密 BYO 凭据 → 避免任何种子/演示数据触发真实 LLM。（设计预期，正向）
3. antd 按钮 accessible name 插入空格/图标前缀 → 已由 `common.steps.ts` 的 `looseName` 宽松正则统一兼容，22/22 E2E 通过。（已解决）

## 门 2 · ddd-phase-quality-gate（结构化审计 12 类）

| 类别 | 结论 |
|------|------|
| DI 注册缺口 | 0 — F28 无新增接口（仅 `IConfiguration`，框架内置） |
| DDD 分层违规 | 0 — 测试在 SpecFlowTests，生产改动在 Infrastructure |
| EF Core 映射缺口 | 0 — F28 无新增聚合/VO |
| 硬编码值 | 0 — "Stub" 为配置比较，"gpt-4o" 为既有默认；测试种子 Guid 属夹具合理范围 |
| 缺失 CancellationToken | 0 — `ResolveAsync(Guid, CancellationToken)` 完整透传 |
| 缺失修饰符 | 0 — `internal sealed` 到位 |
| 并发/生命周期风险 | 0 — 无新增 Singleton/grow-only 集合 |
| 缺失空守卫 | 0 — 值类型参数 + 注入非空 `_configuration` |
| API 基础设施 | 0 — F28 未改 API 层 |
| 蓝图漂移 | 0 — BDD 覆盖为测试特性，已在设计文档登记 |
| 缺失 XML 文档 | 0 — 既有类型文档完整 |
| 死代码 / 休眠常量 | 0 — E2E step 定义与 feature 一一对应，无未引用步骤（带 `{string}` 参数步骤经人工核对全部被 feature 引用） |

**Gate Status: PASS（P0=P1=P2=P3=0）**

## 门 3 · codebase-optimizer（七维度分析 F28 增量）

> 采用分析模式（不建 `codebase-optimizer/{date}` 分支、不 push），以遵守 F28 feature-builder 硬约束（固定在 `feat/f28-bdd-coverage`、不 push）。

| 维度 | 结论 |
|------|------|
| 架构 | 0 — 测试按 BDD 约定组织（feature/steps 分离，fixtures 共享）；无新架构引入 |
| 代码质量 | 0 — `common.steps` 共享、`looseName` 辅助消除重复；`create-agent.feature` 与 `agent-crud.feature` 部分场景重叠属有意冒烟转换 |
| 正确性 | 0 — 选择器经 22/22 E2E 实证；生产守卫经单测实证 |
| 测试 | 0 — 114 后端 + 22 前端 + 124 单元，断言有意义（真实 stub 回复文本、真实 UI heading、租户隔离 Id） |
| 性能 | 0 — 单 worker 规避 Edge 多实例内存崩溃；22 场景 52s，无回归 |
| 安全 | 0 — 无硬编码密钥；`appsettings.Integration.json` 仅 Stub 配置；测试 ApiKey 为夹具 |
| 工程化 | 0 — `bddgen`+`playwright` 已接线；无 `dangerouslySetInnerHTML`/XSS |

桩代码替换进度：后端已实现（`TenantModelClientResolver` Stub 短路为测试契约，非未完工桩）；前端无对应清单 → N/A。

**结论：PASS（0 open）**

## 约束遵循

- 三道质量门对 F28 增量均为 0 open。
- `.quality-gate.json` 已推进至 `f28-bdd-coverage`，保留 `bdd:PASSED` + `frontendE2e:BDD` + `cleared:true` + `codebaseOptimizer` 字段。
- commit message 含 `Quality-Gate:` 行；**不 push**（feature-builder 硬约束）。
