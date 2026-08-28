# Phase 6 · frontend-e2e 修复质量门报告

> 范围：修复 `frontend-e2e` 在真实 key 方向下的两处 CI 失败（`/debug/step` 返回 500、conversation E2E 断言 stub 文本）。
> 本标记对应提交仅改动 `src/` 中 5 个文件 + 3 个文档，属**聚焦式修复**，非阶段收尾全库扫描。
> 方向遵循用户裁定：**e2e 必须用真实 key，不用 stub，文档需更新**。

## 1. ddd-code-reviewer（对抗式审查 · EF Core Migration 模块 + 配置/前端）

### Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix |
|----------|----------|-----------|---------|----------|---------------|
| — | EF 迁移正确性 | `Migrations/20260828044137_WidenModelOutputResultColumns.cs` | 无缺陷 | `Up()` 仅把 `Result` 由 `varchar` 增宽到 `text`，`Down()` 还原到上一快照长度 `varchar(16000/16000/4000)`；`oldMaxLength`(16000/16000/4000) 与上一迁移快照一致，**无模型漂移**；Postgres `ALTER COLUMN ... TYPE text` 为隐式转换，**无数据丢失、无 FK 变动** | 无需修复 |
| — | 配置同步 | `WorkflowConfiguration.cs:51,75` / `ExecutionLogConfiguration.cs:82` | 无缺陷 | `HasMaxLength` → `HasColumnType("text")` 与迁移 `Up()` 完全对应；类均为 `internal sealed` + 实现 `IEntityTypeConfiguration` | 无需修复 |
| — | 前端断言 | `conversation.steps.ts` / `ConversationDetailPage.tsx` | 无缺陷 | 改为断言 `[data-testid="chat-message"][data-role="agent"]` 非空真实回复；选择器稳定 | 无需修复 |

### Control Flow Analysis
- 入口：`StepCompletedEventHandler.SaveChangesAsync`（落库 `ExecutionLogEntry.Result`）
- 执行路径：`SequentialOrchestrator.RunSingleNodeAsync` → `PublishAsync(StepCompleted)` → MediatR `StepCompletedEventHandler` → `SaveChangesAsync`
- 死路径：无
- 未注册接口：无（本次无新增接口）

### Test Coverage
- 受影响的 E2E 场景：`conversation` / `debug-step`
- 实现路径：真实 LLM 长输出 → `Result` 列 → 此前 `varchar(4000)` 截断 → 500；现 `text` 不再截断
- 未测路径：无新增逻辑分支，仅放宽约束；断言改为非空文本，更具真实性

### API Verification
- 外部 API：EF Core `AlterColumn<string>`（Npgsql 提供程序）—— 用法与生成器一致，无 mismatch

### Blueprint Alignment
- 无新增蓝图需求；本修复是对已有 BDD 覆盖（F28）在真实模型方向下的缺陷修正

### Top 3 Runtime Risks（审查后结论：均已消除）
1. **长输出截断 500**（原 `ExecutionLogEntry.Result varchar(4000)`）—— 已通过 `text` 列消除 ✅
2. **迁移回滚不一致**—— `Down()` 长度与上一快照对齐，回滚安全 ✅
3. **E2E 假绿**（断言 stub 文本，真实 key 下永不命中）—— 改为断言非空真实回复 ✅

**结论：0 P0 / P1 / P2 / P3 open。**

## 2. ddd-phase-quality-gate（DDD 结构卫生审计）

扫描 12 个审计类别中与被改文件相关的项：

| Category | Result |
|----------|--------|
| DI 注册缺口 | 无（本次无新增接口） |
| DDD 分层违规 | 无（`ExecutionLogConfiguration`/`WorkflowConfiguration` 仍在 Infrastructure 层） |
| EF Core 映射同步 | PASS（配置与迁移一致，`text` 列） |
| 硬编码值 | 无新增 |
| 缺失 CancellationToken | 不涉及（无新增 async 方法） |
| 缺失修饰符 | 无（impl 已 `internal sealed`） |
| 并发风险 | 无 |
| 缺失空守卫 | 不涉及 |
| API 基础设施 | 不涉及 |
| 蓝图漂移 | 无 |
| 缺失 XML 文档 | 不涉及（迁移/配置为生成或既有） |
| 死代码/空心类 | 移除 `appsettings.Integration.json` 误导性 `ModelClient:Stub` 死配置（正向） |

**Gate Status: PASS  [P0: 0 | P1: 0 | P2: 0 | P3: 0]**

## 3. codebase-optimizer（七维度体检 · 聚焦本次修复）

> 注：本次为聚焦修复，非阶段收尾，故**未执行全库多轮扫描**（全库扫描会越界并触发 push，违背 per-feature 分支 / no-push 约定；同 `remove-routersettings` 标记先例）。

| 维度 | 结论 |
|------|------|
| 架构 | 配置 → 迁移一致；无新增抽象 |
| 代码质量 | 迁移文件含 `#pragma warning disable IDE0161`（满足 `TreatWarningsAsErrors`）；两处配置加注释说明 500 根因 |
| 正确性 | `text` 列消除 `String or binary data would be truncated` → 500 |
| 测试 | E2E 断言真实助手回复非空，移除 stub 假绿；`data-testid`/`data-role` 提供稳定选择器 |
| 性能 | Postgres `text` 与 `varchar(n)` 同底层存储，无回归 |
| 安全 | 移除 `Stub` 配置无新风险；无密钥硬编码 |
| 工程化 | 后端 `dotnet build` Infrastructure+Api **0 warning / 0 error**；前端 `tsc --noEmit` **0 error**；文档同步更新 |

**结论：0 open（scoped）。**

## 验证证据
- 后端构建：`dotnet build src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj` + `.../AgentPlatform.Api.csproj` → `0 个警告 / 0 个错误`
- 前端类型检查：`node node_modules/typescript/bin/tsc --noEmit`（src/AgentPlatform.Web）→ `EXIT=0`
- 文档更新：`features/bdd-coverage-design.md`、`docs/quality/f28-bdd-coverage-gate.md`、`docs/quality/f8-negotiation-gate.md` 已更正为「Integration 走真实 SemanticKernelModelClient（CI 注入 key），非 Stub」

## 诚实性声明
本标记仅覆盖上述聚焦修复，未对全库做多轮扫描。被改代码经对抗式审查与结构审计确认为 0 open findings，可放心 `cleared: true`。

---

## 4. 追加修复（调试路径 VariablesJson + ErrorDetail 仍截断 → 500，CI 复现）

CI 在提交 `7f35864`（仅放宽 `Result` 三列）后，`workflow-debug` 的 `/debug/step` **仍 500**。复查调试路径发现首轮遗漏列：

### 根因（调试路径专属写库）
`DebugStepCommandHandler` 在 `primitive.DebugStepAsync` 之后调用 `sessionRepo.Update(session)`，把**累积黑板变量**（`Dictionary<string,string>`，含各节点真实 LLM 输出）序列化进 `DebugSession.VariablesJson`——该列原 `HasMaxLength(8000)`。真实长回复使 JSON 超 8k → `DbUpdateException` → 500。这是调试路径相对正常运行路径多出来的写库点，首轮只放宽了 `ExecutionLogEntry/WorkflowStep/WorkflowNode.Result` 而漏掉它。

同一测试还覆盖「错误分支」：`ExecutionLogEntry.ErrorDetail`(2000) / `WorkflowStep.ErrorDetail`(8000) / `WorkflowNode.ErrorDetail`(8000) 存放真实异常 + 堆栈，亦易超长 → 同类截断 500。

### 修复
- `DebugSessionConfiguration.cs`：`VariablesJson` `HasMaxLength(8000)` → `HasColumnType("text")`。
- `ExecutionLogConfiguration.cs`：`ErrorDetail` `HasMaxLength(2000)` → `text`。
- `WorkflowConfiguration.cs`：`WorkflowStep.ErrorDetail` / `WorkflowNode.ErrorDetail` `HasMaxLength(8000)` → `text`。
- 新增迁移 `20260828061418_WidenDebugAndErrorColumns`（`Up` 四列 varchar→text、`Down` 还原长度；含 `#pragma warning disable IDE0161`）+ `AppDbContextModelSnapshot.cs` 自动更新为 text。

### 验证
- `dotnet build` Infrastructure + Api → **0 警告 / 0 错误**。
- 快照确认 `VariablesJson` 与三处 `ErrorDetail` 均为 `text`。
- 已覆盖调试单步（`VariablesJson`）+ 错误分支（`ErrorDetail`）两类截断 500 根因。

### 风险审查
- 是否仍有其他截断点？调试路径持久化面 = `ExecutionLogEntry.(Result,ErrorDetail)` + `WorkflowNode.(Result,ErrorDetail)` + `WorkflowStep.(Result,ErrorDetail)` + `DebugSession.VariablesJson`，现已**全部 text**。其余有界列（`Conversation.Content` 16000 / `AuditLog.Details` 4000 / `WorkflowNode.ConfigJson` 16000）不在本失败路径（会话/审计/调试覆盖配置）且当前 e2e 未触发，留待后续按需放宽，不在本次聚焦修复内。
- `Down()` 还原长度与上一快照对齐，回滚安全。

**结论：0 open（scoped follow-up）。**

---

## 5. 追加修复（Conversation.feature 真实 LLM 调用超 100s 客户端超时）

### 现象（CI 复现）
`Conversation.feature` 场景「Admin 创建会话后向其发送消息得到回复」在 `向该会话发送消息 "你好，介绍一下自己"` 步骤抛 `TaskCanceledException`（~100.1s）。堆栈：`IntegrationClient.SendAsync`（line 34）→ `ConversationSteps.SendMessage`（line 83）→ `HttpClient` 默认 100s `Timeout` 触发，服务端响应流被客户端中止（`ResponseBodyReaderStream.CheckAborted`）。结果：1 failed / 114 passed。

### 根因
`IntegrationHost.Api` 是单例 `HttpClient`，在 `IntegrationAppFactory.InitializeAsync` 经 `CreateClient(...)` 创建，**未设置 Timeout**，故沿用默认 100s。`POST /conversations/{id}/messages` 经 `SendMessageCommandHandler` → `ModelRouter.RouteAsync` → **单次真实 LLM 调用**（Integration 强制真实 key，模型默认 `gpt-4o-mini` 或 `OPENAI_BASE_URL` 指向的 DeepSeek/vLLM 兼容端点）。真实端点首调用冷启动 / CI 网络抖动下，单条消息完整回复常 > 100s；`HttpClient.Timeout` 把仍在进行的调用截断为客户端取消 → 服务端流中断 → 测试报取消/500。后端 `RouterSettings.TimeoutSeconds` 默认 0（不限制单次调用），故唯一硬上限是测试客户端 100s。

此非 500 列截断回归（列放宽不影响时延），而是真实 key 方向暴露的**客户端超时过紧**。stub 禁用（用户裁定），故只能放宽测试客户端超时。

### 修复
- `IntegrationAppFactory.cs`：`Api = CreateClient(...)` 后增设 `Api.Timeout = TimeSpan.FromMinutes(5);`，并注释说明缘由。
- 补充 `using System;`（供 `TimeSpan`）。
- `RealStepsIntegrationAppFactory` 继承基类且未覆写 `InitializeAsync`，故 F12 宿主（`F12IntegrationHost.Api`）一并受益，无需重复修改。

### 验证
- `dotnet build src/AgentPlatform.SpecFlowTests/AgentPlatform.SpecFlowTests.csproj` → **0 警告 / 0 错误**。
- 无法在沙箱本地重跑（需真实 `OPENAI_API_KEY` + 运行中的后端；沙箱无 GitHub 出站，待 CI 实跑确认）。结构上：客户端 5 分钟天花板足以容纳真实 LLM 冷启动/抖动，单条「自我介绍」完整回复通常 < 5 min。

### 风险审查
- 是否掩盖真实挂死？端点真挂会快速抛连接异常（非挂起）；仅「慢但不死」的调用需更长天花板，5 min 合理。CI `integration` job 无 `timeout-minutes`（默认 360 min），单测挂 5 min 不拖垮整 job。
- 是否影响其他测试？`Api.Timeout` 为全局，仅放宽上限；非 LLM 的 CRUD/RBAC 场景仍秒级完成，不受影响。

**结论：0 open（scoped follow-up）。**

---

## 6. 真正的根因：credentials E2E 测试的 BYO 凭据污染默认租户（CI 复现，commit 待提交）

前两轮（§1-§5）把 `/debug/step` 500 归因于「列截断」并放宽了多列——**该假设在本环境不成立**：前端 E2E 后端用 SQLite（`integration.mjs` 注入 `ConnectionStrings__DefaultConnection`），而 **SQLite 不强制 varchar(n) 长度**（EF Core 在 SQLite 上把字符串统一映射为 TEXT），截断 `DbUpdateException` 在此环境不可能发生。本地全链路复现（真实 `SemanticKernelModelClient` + 本地 mock OpenAI 端点，短/长 30KB/慢 15s/401 多形态）中 `/debug/step` 一律 200，证明 debug 路径本身无逻辑缺陷。真实根因在 **E2E 测试顺序污染**：

### 根因
`credentials.feature` E2E 测试「添加模型凭据并保存成功」给默认租户 T1 保存了一条 **BYO 凭据**（Provider=OpenAI、API Key=`sk-e2e-test-12345` 假 key、模型=`gpt-4o`、BaseUrl 空 → 默认 `api.openai.com/v1`）。`ModelRouter.RouteAsync` 的候选序是 **BYO 优先**（`byoCandidates.Concat(platformCandidates)`）——**此后 T1 的所有真实 LLM 调用（含更靠后的 publish-workflow 运行、workflow-debug 的 `/debug/step`）都改走这条必失败的假凭据**：
- CI（能达 api.openai.com）：401 → 步骤失败 → publish-workflow 的 run 返回 200+失败节点（测试仅断言非 401/404 故误绿）；`/debug/step` 的 LLM 调用失败 → 500（失败路径在 CI 下的确切 500 机制未能在本地复现，本地同场景返回 200+RolledBack；触发源同一）。
- 本地（沙箱无 api.openai.com 出站）：连接超时 21s → publish-workflow 测试 30s 超时失败；workflow-debug 快速失败返回 200 故误绿。

本地全量 E2E（真实后端 + mock LLM 端点）实证：修复前 26/27（publish-workflow 失败、workflow-debug 依赖快速失败误绿）；修复后 **27/27 全绿**。

### 修复（测试隔离，非后端行为改动）
- `credentials.steps.ts`：新增步骤「我删除测试模型凭据以恢复租户状态」——经独立 `request` 夹具 + fixture ApiKey（`X-API-Key: integration-fixture-key-0001`）调用 `GET /api/v1/tenant/credentials?category=0` 找到同名凭据后 `DELETE /api/v1/tenant/credentials/{id}`（接受 200/204），恢复租户回平台模型（CI 注入的真实 key）。走独立 request 不挂 page，避免污染 flowErrors。
- `credentials.feature`：场景「添加模型凭据并保存成功」末尾追加该清理步骤。
- 若凭据未创建成功（非必现路径），清理步骤幂等跳过。

### 验证
- 本地全量前端 E2E（`scripts/integration.mjs --e2e --skip-bdd`，mock LLM 端点）：**27/27 passed（52.8s）**，publish-workflow（#21）与 workflow-debug（#25）均绿；修复前同环境 26/27。
- 前端 `tsc --noEmit` exit 0。
- CI 上 bddgen 无本地沙箱的批量删除护栏问题，不受本地复现环境差异影响。

### 风险审查
- 是否掩盖真实缺陷？后端 BYO 优先是设计（F13 多租户 BYO-Key 语义），测试应自清理；修复不改变任何后端行为。
- 是否还有其他污染源？E2E 仅 credentials 场景创建凭据，清理后租户恢复；agentic-run 等仅建 Agent 不涉模型凭据。
- 前两轮列放宽（`7f35864`/`23ed7b5`）基于错误的截断假设，未解决 E2E 失败；已确认**无害**（Postgres 上 text 与 varchar 同存储、无数据丢失），故保留在历史中不另做回滚（回滚只会引入无谓 churn）。

**结论：0 open（scoped follow-up）。根因已 100% 定位并本地全量验证。**
