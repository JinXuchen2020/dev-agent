# 全量项目分析报告 — Agent 编排平台 (dev-agent)

- 日期：2026-09-22
- 基线：`master` @ `b8359fa`（Merge PR #33 `feat/workflow-flex-edit`）
- 方法：读源码 + 读门禁标记 + 读 CI 配置 + `git` 实测，**不采信文档自述结论**，每条判断均给出可复现的核验命令或文件锚点
- 对比：本报告接续 `docs/code-analysis-2026-07-20.md`（两个月前）

---

## 1. 项目定位

企业级、强类型、自研的 Agent 编排平台。技术栈 .NET 9 + DDD + Clean Architecture（后端）与 React 19 + Vite 8 + TypeScript strict + Antd 5 + @xyflow/react（前端）。

不是"调一次大模型 API 的 demo"，而是**带完整平台化外设的编排内核**：多租户、RBAC、沙箱隔离、持久化执行、队列化、可观测性、在线评估门禁均已落到代码。

### 量化画像

| 维度 | 实测值 | 备注 |
|------|--------|------|
| 后端 C# 文件 | 751 个 | 含测试项目 |
| 后端代码行 | 104,408 行 | 其中迁移 40,629 行（63 个文件） |
| 后端业务代码 | 63,779 行 | 扣除 `*Migrations*` |
| 前端 TS/TSX | 72 个文件 / 14,805 行 | `src/` 下 |
| 单元/集成测试 | 520 个 `[Fact]`/`[Theory]`，分布在 89 个文件 | |
| 后端 BDD | 106 个 Gherkin 场景（Reqnroll） | 覆盖 8 个功能域 |
| 前端单测 | 14 个 vitest 文件 | |
| 前端 E2E | 29 个 playwright-bdd 场景 / 17 个 `.feature` | |
| 领域模型 | 30+ 聚合根、6 个值对象、22 个仓储接口、5 组领域事件 | |
| 文档 | `docs/` 86 + `features/` 50 + `phases/` 11 + `appendices/` 10 | 约 157 份 Markdown |
| 阶段 | Phase 1–6 完成；F1–F43 功能史诗 42 done / 5 open | |

**分层依赖**：`Api → Application → Domain`，`Infrastructure → Application`。Domain 零外部包依赖，由架构测试强制。

---

## 2. 优点

### 2.1 架构纪律是"被强制"的，不是"被声称"的

`src/AgentPlatform.ArchitectureTests/DddLayerTests.cs` 用 ArchUnitNET + 文件级断言，把 6 条 DDD 规则变成**会失败的测试**：

1. Domain 项目不得含任何 `PackageReference` / `ProjectReference`
2. Application 不得引用 Infrastructure
3. Infrastructure 实现类必须 `internal sealed`
4. 每个聚合根必须有对应 `IEntityTypeConfiguration`
5. `Application/Abstractions` 下每个接口必须在 DI 中注册
6. Controller 不得直接注入应用服务（只许 `IMediator` + 白名单横切设施）

配合 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` + `Nullable=enable` + `EnforceCodeStyleInBuild=true` + `AnalysisLevel=latest`，编译期就把质量下限焊死。**这是本项目最扎实的一块。**

### 2.2 质量治理闭环是真实的，且有机器可校验的落点

四道关：设计评审关 → `ddd-code-reviewer`（代码保真）→ `ddd-phase-quality-gate`（结构卫生）→ `codebase-optimizer`（全库健康）。

关键在于它**不靠自觉**：

- 仓库根 `.quality-gate.json` 记录 `cleared: true` + 三份结论 + 报告引用
- `scripts/git-hooks/pre-commit` 校验暂存区含 `src/` 时该标记必须已暂存且 `cleared: true`
- `.github/workflows/ci.yml` 的 `quality-gate` job 用 `git diff --name-only "$BASE" HEAD | grep '^src/'` 复现同一校验

### 2.3 测试金字塔是真的四层，不是"有测试文件"

单元（520 例）→ 架构（9 例）→ 集成（5 例）→ 后端 BDD（106 场景）→ 前端 vitest（14 文件）→ 前端 E2E（29 场景）。CI 用 `--filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~SpecFlowTests"` 精确切分作业。

### 2.4 领域建模有真实厚度

`Domain/Aggregates/` 下 30+ 聚合根，覆盖 `Workflow`/`ExecutionLog`/`AgentRun`/`HumanApproval`/`EvaluationDataset`/`PublishedWorkflow`/`TenantCredentialSetting`/`Workspace` 等；值对象 `Money`、`TokenUsage`、`AgentType`、`ModelEndpoint`、`ConfigurationVersion` 都是行为化的，不是 DTO 换皮。工作流聚合还带 5 组领域事件（`WorkflowStarted`/`StepCompleted`/`StepFailed`/`WorkflowCompleted`/`WorkflowRolledBack`）。

### 2.5 生产级外设的覆盖面超出同类自研项目

| 能力 | 实现位置（抽样） |
|------|------------------|
| 认证 | JWT（httpOnly + SameSite Cookie）+ API-Key Smart policy scheme |
| 授权 | RBAC + `ApiKeyAuthenticationHandler` |
| 多租户 | `ITenantScoped` + EF Global Query Filter，**两层**（租户 + 工作空间） |
| 密钥安全 | AES-256-GCM 加密存储 + `KeyRotationService` + `ApiKeyExpiryJob` |
| 防护 | 限流（含 `PerApiKey` 分区）+ 提示注入中间件 |
| 审计 | `AuditLog` 聚合根 |
| 沙箱 | 双层隔离：Docker 强隔离 + JobObject/AppContainer 兜底（`Sandbox/`） |
| 持久化执行 | 检查点 + 恢复（F30） |
| 队列化 | 三后端（InMemory / Redis / RabbitMQ）+ worker 消费（F37） |
| 可观测 | Prometheus + 9 条告警规则 + 12 面板 Grafana + Alertmanager |
| 质量门禁 | 在线评估数据集 + CI 阻断模板（GitHub / GitLab 双份） |
| 成本 | `Money` / `TokenUsage` 值对象 + 模型路由降级 |

### 2.6 工程诚实度罕见地高

`CHANGELOG.md` 里能看到"诚实性校正""已知残留（非阻断）""waiver"这类主动披露；`.quality-gate.json` 的 `notes` 字段甚至记录了自己踩的坑（例：命令声明 `IRequest` 而非 `ICommand` 导致 `UnitOfWorkBehavior` 不提交、端点假成功不落库）。还专门写了 `docs/blueprint-drift-postmortem.md` 复盘文档与实现的漂移。这种自我纠错文化比代码本身更难复制。

### 2.7 前端工程化基础扎实

TypeScript strict、`TODO/FIXME/HACK` 零命中、`any` 仅 10 处、ESLint 配置完整、i18n 中英双语对称、`ErrorBoundary` + 404 兜底 + `AbortController` 请求取消均已落地。

---

## 3. 缺点与风险

按严重度排序，全部为**实测发现**。

### 🔴 P0-1：生产不可部署 —— 无 Dockerfile、compose 无 api 服务

```bash
find . -name "Dockerfile*" -not -path "*/node_modules/*"   # → 0 结果
```

`docker-compose.yml` 只有 `postgres` / `redis` / `rabbitmq` 三个中间件，**没有 api 服务**。全仓库不存在任何 Dockerfile。API 只能 `dotnet run` 裸跑。这意味着整套平台目前**没有可复现的交付形态**。（对应 `features/backlog.md` F46，状态 open）

### 🔴 P0-2：F44 未闭环 —— 对外 API/MCP 发布链路实际不可用

实测：

```bash
find src -name "ApiKeysController.cs" | wc -l     # → 0
grep -rn "ApiKeysPage" src/AgentPlatform.Web/src  # → 仅定义处，无任何 import
```

- 前端 `pages/ApiKeysPage.tsx`（97 行）是**不可达孤儿页**：无路由、无导航入口、全仓无 import；rotate/revoke 仍是占位 toast。
- 后端**完全没有 api-keys 的管理端点**（校验/轮换/过期的基建齐全，但没有创建入口）。
- 后果：`PublishedWorkflowsController` 与 `McpController` 均为 `[Authorize(AuthenticationSchemes="ApiKey")]`，全局唯一可用的 key 是 `DatabaseInitializer` 在 Integration 环境播种的明文 dev key → **除集成测试外，没有任何调用方能自助签发凭证**。

即 F22「发布工作流为 API/MCP」这个卖点，在当前基线不可生产使用。

### 🟠 P1-1：CI 里的集成测试被永久关闭

`.github/workflows/ci.yml:73`：

```yaml
- name: Run integration tests
  if: false  # Integration tests need Docker. Enable when Docker is available on runner.
```

`if: false` 是硬关闭。`AgentPlatform.IntegrationTests`（5 例，需真实 `OPENAI__Key`）**从未在 CI 跑过**。CI 声称的绿色，实际覆盖不到这层。

### 🟠 P1-2：本地质量钩子未启用，治理闭环只靠 CI 兜底

```bash
git config core.hooksPath    # → 未配置（空）
```

`scripts/git-hooks/{pre-commit,commit-msg}` 写得完备，但 `core.hooksPath` 没设，**本地根本不生效**。README 承认需要手动执行 `git config core.hooksPath scripts/git-hooks` 或 `scripts/install-hooks.ps1`。也就是说日常开发中，那套引以为豪的门禁实际上是"事后 CI 拦截"，而不是"提交前拦截"。

### 🟠 P1-3：EF Core 迁移历史被切成两个目录

```bash
src/AgentPlatform.Infrastructure/Migrations            : 57 个文件（2026-07-14 → 2026-09-01）
src/AgentPlatform.Infrastructure/Persistence/Migrations :  6 个文件（2026-08-24 → 2026-08-25）
```

主时间线 7/14–9/1 中间，8/24–8/25 有 3 个迁移（`AddDurableExecutionCheckpoint`、`AddRunningExecution`、`AddAgentMessageLog`）掉进了 `Persistence/Migrations` 这个旁支命名空间。EF 按 `[Migration]` 特性 + 时间戳排序，通常仍能正确应用，但这是明确的**约定破坏**：`dotnet ef migrations add` 默认只往 DbContext 所在命名空间的 `Migrations` 写，很容易继续分叉；人工 review 时也无法一眼判断迁移全序。

### 🟠 P1-4：迁移代码占比 39%，且缺少基线压缩

63 个迁移文件 = 40,629 行，占后端总代码的 38.9%。项目仍在高速演进阶段，但从未做过一次 squash（基线快照 + 清空历史），新克隆者要读完 63 个迁移才能理解 schema 演化。同时这是 `codebase-optimizer` 全库扫描里最容易掩盖真实业务代码规模的部分。

### 🟡 P2-1：README 与 `package.json` 脚本不符（文档漂移）

README 第 57 行让用户执行：

```bash
npm run typecheck && npm run build
```

而 `src/AgentPlatform.Web/package.json` 的 `scripts` 只有 `dev` / `build` / `preview` / `e2e` / `e2e:ui` —— **不存在 `typecheck`，也不存在 `lint` 和 `test`**。但 `features/backlog.md:19` 又声称"`typecheck/lint/build/unit/e2e` 五道闸门当前全绿"。

实际后果：lint 与 vitest 单测**没有可执行的 npm 入口**，14 个 vitest 文件只能靠手工 `npx vitest` 跑，CI 里也没有前端单测作业。

### 🟡 P2-2：台账（backlog）自身状态过期且自相矛盾

`features/backlog.md` 同一文件里有两套互斥统计：

- 第 13–14 行（2026-08-30）：46 条史诗，done 36 / open 10，并列出 `F35/F36/F37/F38/F39/F40` 为 open
- 第 21–22 行（2026-09-04）：47 条史诗，done 42 / **done⚠️未合并 7** / 待做 5

而 `git log` 实测 `f92b1e9 F35`、`52fd9bf F36`、`3ed615b F37`、`88255a8 F38`、`e2f6ddd F39`、`2af39ed F40` **全部已在 master 上**。所以"7 项未合并、基线不可用"的结论已经不成立，属于**台账未随合并同步**。在一个把"文档驱动"当红线的项目里，主台账失真会直接误导后续排期。

### 🟡 P2-3：E2E / 集成测试强依赖真实付费 LLM

README 与 CI 均要求 `OPENAI_API_KEY` 真实 key。后果：

- 测试非 hermetic，网络/配额/供应商波动都会造成假失败（`CHANGELOG` 里已多次出现"唯一失败=master 既有 LLM 用例"这类记录）
- 每次 CI 跑 E2E 都在真实计费
- 本地开发门槛高。`.quality-gate.json` 的 notes 直接承认："本地 E2E 本环境不可跑（vite dev/build 被 CLI 注入的 node-safe-delete-shim 批量删除守卫阻断，沙箱放行无效）→ 交 CI"

### 🟡 P2-4：仓库残留物

| 路径 | 状态 | 问题 |
|------|------|------|
| `src/AgentPlatform.Api/appsettings.QuickStart.json` | **被 git 跟踪** | F41 已移除 QuickStart 模式，该配置文件成死配置 |
| `src/AgentPlatform.Api/agent_platform.db`(+`-shm`/`-wal`) | 未跟踪 | 本地 SQLite 落库产物 |
| `src/AgentPlatform.Application/bin.bak-1787977398595/` | 未跟踪 | 时间戳后缀的备份目录 |
| `.stale-build-artifacts/` | 未跟踪 | 陈旧构建产物 |
| `backend-dev.log` / `frontend-dev.log` / `build-release.log` | 未跟踪 | 根目录裸日志 |
| `src/AgentPlatform.Api/AgentPlatform.Api.csproj.user` | 未跟踪 | IDE 个人配置 |

虽未污染版本库，但说明 `.gitignore` 与日常清理习惯有缺口。另外工作区当前有未提交改动（`appsettings.json` 改了 4 增 15 删）。

### 🟡 P2-5：前端状态层偏薄

zustand 主要承担请求缓存，缺少执行态快照与乐观更新。工作流长任务（ReAct 循环、durable execution 恢复）在 UI 侧的体验依赖 SSE 推送补位，断线/刷新后的状态重建能力弱。

### 🟡 P2-6：文档投入与交付能力的错配

157 份 Markdown（`docs/` 86 + `features/` 50 + 其余）对 751 个源文件。文档纪律本身是优点，但**当 P0 的"不能部署"（F46）与"发不出 key"（F44）同时 open 时**，说明资源分配更偏向"过程完备"而非"交付闭环"。

### 🟡 P2-7：架构测试的实现方式偏"文本匹配"

`DddLayerTests.cs` 中多条规则实际是读 `.csproj` 文本 / 用正则扫源码（如 `@"\b(I\w+)\s+\w+\s"` 匹配注入）。这带来两个问题：

- 误报需要靠 `allowed` 白名单 + 注释剥离 + 字符串字面量剥离来打补丁（见第 207–219 行），每次新增合法注入都要改测试
- 断言强度弱于 ArchUnitNET 原生的类型级规则（例如"必须 internal sealed"实际靠的是 CA1852 编译器规则，测试只是"意图声明"，注释里自己承认了）

另外每个测试开头都有 `if (!dir.Exists) return;` —— 目录找不到就**静默通过**。这在 CI 上是"假绿"通道。

---

## 4. 优化方向

### 第一优先级：把"能交付"补上（对应 P0）

1. **F46 生产部署编排**（最高性价比）
   - 多阶段 `Dockerfile`（build + runtime，`USE_POSTGRESQL` 条件编译切换）
   - `docker-compose.yml` 增加 `api` 服务：连接串 + `OPENAI__Key` 环境变量映射 + healthcheck
   - 前端产物静态托管（nginx 容器 或 api 静态文件中间件，二选一）
   - `docs/` 补部署指南（环境变量清单 / 升级 / 备份）
   - 验收：`docker compose up` 一键起完整栈，健康检查通过，真实 key 注入后对话与工作流跑通

2. **F44 API Key 生命周期闭环**（解除 F22 生产阻断）
   - 后端 `GET/POST/DELETE /api/v1/api-keys`（+ `POST /{id}/rotate`），创建/轮换**一次性明文返回**，AES-256-GCM 存储 + 租户隔离 + RBAC（Admin/Operator），禁止明文落库
   - 前端 `ApiKeysPage` 接真实 CRUD + 错误兜底，接入 `App.tsx` 路由与 `AppLayout` 导航
   - `WorkflowsPage.tsx:248` 的 `.catch(() => setApiKeys([]))` 静默吞错改为显式错误态
   - E2E：创建 key → 发布工作流绑定 → 外部带 `X-API-Key` 调通

3. **修复 CI 假绿通道**
   - 打开 `ci.yml:73` 的集成测试（runner 装 Docker 或改为 Testcontainers）
   - 给 `DddLayerTests` 的 `if (!dir.Exists) return;` 改成 `Assert.True(dir.Exists, "...")`，杜绝静默跳过
   - 把 `npm run lint` / `npm run test` 补进 `package.json` 并在 CI 加前端单测作业

### 第二优先级：消除可维护性隐患（对应 P1）

4. **合并迁移目录**：把 `Persistence/Migrations` 的 3 个迁移迁回 `Infrastructure/Migrations`（或反向统一），并在 `docs/database-conditional-compilation.md` 写明约定，避免再分叉。
5. **迁移基线压缩**：在下一个稳定点做一次 squash（生成 `InitialBaseline` 快照，归档历史迁移）。可把 4 万行迁移压到几千行，显著降低新成员理解成本。
6. **启用本地钩子**：`scripts/install-hooks.ps1` 应在 README 快速开始里作为**必做步骤**前置，而不是可选提示。或改走 `Directory.Build.targets` / CI 双保险，避免"依赖开发者记得配"。
7. **同步 backlog 台账**：清理 `features/backlog.md` 中并存的两套统计，按 `git log` 实测把 F35–F40 标为已合并，只保留单一权威统计块。建议把该统计块做成 CI 校验（`open` 数与 `features/*.md` 实际状态对账）。

### 第三优先级：质量与体验补强（对应 P2）

8. **让测试 hermetic**：为 LLM 调用引入录制回放（如 VCR 模式：录制真实响应 → CI 用回放跑），把真实 key 只留给少量 nightly 冒烟。这能同时解决成本、稳定性、本地门槛三个问题。
9. **前端状态层厚化**：执行态快照 + 乐观更新；SSE 断线后基于执行日志重建视图（F40 的回放能力可作为数据基础复用）。
10. **清理残留物**：删除 `appsettings.QuickStart.json`；`.gitignore` 补 `*.log`、`bin.bak-*`、`.stale-build-artifacts/`、`*.db*`、`*.csproj.user`。
11. **架构测试升级为类型级断言**：逐步用 ArchUnitNET 原生 fluent API 替换文本/正则匹配，去掉 `allowed` 白名单这类补丁式维护。
12. **文档瘦身与分层**：`docs/` 86 份需要一次目录治理（按 `learning/` `quality/` `guides/` 分区，归档过期分析），避免读者在 157 份文档里找不到入口。

---

## 5. 一句话结论

**架构与质量治理属于同类自研项目里的第一梯队，问题不在"做得糙"，而在"做完了但交不出去"**——F44（发不出 API Key）与 F46（没有 Dockerfile）两个 P0 挡在生产门口，同时 CI 里集成测试被 `if: false` 关闭、本地质量钩子未启用，让"全绿"的可信度打了折扣。补齐交付闭环 + 修掉三处假绿通道，这个项目的实际价值会有台阶式提升。

---

## 附：核验命令清单

```bash
# 代码规模
find src -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | wc -l            # 751
find src -name "*.cs" -not -path "*Migrations*" -not -path "*/obj/*" -exec cat {} + | wc -l   # 63779
find src -path "*Migrations*" -name "*.cs" -exec cat {} + | wc -l                   # 40629

# 测试规模
grep -rho "\[Fact\]\|\[Theory\]" src --include="*.cs" | wc -l                       # 520
grep -rho "Scenario\(: \| Outline: \)" src/AgentPlatform.SpecFlowTests --include="*.feature" | wc -l  # 106

# P0-1 不可部署
find . -name "Dockerfile*" -not -path "*/node_modules/*"                            # 空

# P0-2 F44 未闭环
find src -name "ApiKeysController.cs" -not -path "*/obj/*" | wc -l                  # 0
grep -rn "ApiKeysPage" src/AgentPlatform.Web/src                                    # 仅定义处

# P1-1 集成测试被关闭
grep -n "if: false" .github/workflows/ci.yml                                        # :73

# P1-2 钩子未启用
git config core.hooksPath                                                           # 空

# P1-3 迁移目录分叉
find src -type d -name "Migrations" -not -path "*/obj/*"                            # 2 个

# P2-1 脚本漂移
grep -n '"typecheck"\|"lint"\|"test"' src/AgentPlatform.Web/package.json             # 无匹配

# P2-4 残留物
git ls-files --error-unmatch src/AgentPlatform.Api/appsettings.QuickStart.json       # TRACKED
```
