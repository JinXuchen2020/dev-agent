# 质量门结论 — 移除 RouterSettings.Candidates，平台模型改为 DB 驱动 PlatformModels

> 提交范围：移除 `RouterSettings.Candidates` / `ModelCandidateConfig`，新增 DB 支持的 `PlatformModels` 目录（实体 + EF 配置 + 迁移 + 种子），平台默认模型由 DB 驱动；同时放宽 `AgentsController.CreateAgent`（模型字段可选，缺省回退平台默认模型）。

## 三道质量门结果

### 1. ddd-code-reviewer（对抗式代码审查）
- 逐文件审查：PlatformModel 实体（非租户隔离、AES-256-GCM 密文落库）、PlatformModelConfiguration、PlatformModelsProvider（DB 读取 + `OpenAI:*` 回退）、PlatformModelClientBuilder（DB 解密注册 + 空表回退 env）、DatabaseInitializer 幂等种子、AgentsController.CreateAgent、DI 注册、迁移。
- 关键风险点逐条核实：
  - **EF 并发**：`PlatformModel.Id` 由调用方生成 + `ValueGeneratedNever()`，无 UPDATE-on-INSERT 陷阱。`private init` 为本仓库 EF 实体既定约定（Agent/Conversation/AuditLog 等同款），物化安全。
  - **加密落库**：种子经 `IApiKeyEncryptionService.EncryptKey` 产密文 + 前缀，`EncryptedApiKey` 永不明文；Builder 经 `DecryptKey` 还原。
  - **空表/缺 Key 双回退**：`PlatformModelsProvider` 与 `PlatformModelClientBuilder` 在表空或行无 Key 时均回退 `OpenAI:*`，保证开箱即用；且二者推导出的 model id 同源（`OpenAI:Model ?? "gpt-4o-mini"`），**不会出现 client 注册键与 router 候选 id 不匹配**。
  - **DI 生命周期**：Builder / `SemanticKernelModelClient` / `IModelClient` 均为 `AddScoped`，Builder 内读取的 `AppDbContext` 同为 Scoped，无捕获泄漏。
  - **种子幂等**：`AnyAsync` 守卫 + `SaveChangesAsync`，异常仅记录告警并继续启动。
- **结论**：0 个 P0/P1；仅 P2/P3（如 `IgnoreQueryFilters()` 冗余、文档措辞），均无害。Agent-create BDD（1/1）与路由 BDD（7/7）**真实通过**（`DisplayName~` 过滤，已纠正此前 `FullyQualifiedName~` 误判 0 测试的偏差）验证 DB 驱动路径。

### 2. ddd-phase-quality-gate（DDD 结构卫生）
- `IPlatformModelProvider` 已在 `Infrastructure.DependencyInjection` 注册（`AddScoped`）。
- Application 层零引用 Infrastructure 命名空间（DDD 合规）。
- 三个新实现类（PlatformModelsProvider / PlatformModelClientBuilder / PlatformModelConfiguration）均为 `internal sealed`。
- `PlatformModel` 含 `IEntityTypeConfiguration`；`Guid Id` `ValueGeneratedNever`；不实现 `ITenantScoped` → 不套用租户查询过滤（与 `WorkflowTemplate` 同策略）。
- 构建 **0 warning / 0 error**。
- **结论**：0 open（PASS）。

### 3. codebase-optimizer（七维度体检，聚焦本提交）
- 架构分层合规；移除死配置 `RouterSettings.DailyBudget`（实际限额由 `PerTenantDailyBudget` 执行，无消费方）。
- 正确性：空目录/env 双回退、幂等种子、加密落库均正确。
- 测试：真实 provider 路径经 Agent-create BDD 验证；迁移 + 种子经集成 fixture 启动验证。
- 安全：AES-256-GCM 密文落库，Key 取自 env，DB 无明文。
- 工程化：迁移 + 快照同步、`#pragma warning disable IDE0161` 满足 `TreatWarningsAsErrors`。
- **结论**：PASSED（Round 1，0 open，scoped to this refactor）。未执行全库多轮扫描——本提交为聚焦重构而非阶段收尾，全库扫描会越界并触发 push，违背项目 per-feature 分支 / no-push 约定。

## 遗留说明
- 真实 LLM 集成 BDD（Conversation.SendMessage）本沙箱无法跑（无 `OPENAI_API_KEY` 且无到 `apihub.agnes-ai.com` 网络）；但模型不匹配 500 根因（Candidates 硬编码 `gpt-4o-mini` vs `OpenAI:Model=agnes-2.5-flash`）已随 DB 种子（CI = `agnes-2.5-flash`）与 client 注册键一致而彻底消除。CI 三变量已配齐，应直接通过。
- 全部改动（含上轮 `AgentsController` / `IntegrationAppFactory` 修复）尚未 push（沙箱网络阻断），仅本地提交。
