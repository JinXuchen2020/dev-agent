# F19 · Agent Roles 内建标记 + 页面补全 + 分类合并 — 质量门报告

> 分支：`feat/f19-agent-roles-unified`
> 日期：2026-07-29
> 三道门：ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer —— 全部 PASS（P0/P1/P2/P3 = 0 open，审查发现的 2×P2 + 2×P3 已当场修复）

## 1. 实现范围（对照 `features/agent-roles-builtin.md` D1–D4）
1. **统一角色目录（DB 为准）**：`AgentRoleDefinition` 表成为唯一权威；`AgentType` 值对象降级为内建目录的类型化镜像（`Predefined` code 与 `BuiltInRoleCatalog` 完全一致）；新增架构 parity 测试（`AgentRoleCatalogParityTests`，3 例）强制两者 code 集合相等。
2. **内建标记**：`AgentRoleDefinition` 增 `IsBuiltIn`(bool) + EF 迁移 `AddAgentRoleIsBuiltIn`（`defaultValue:false`）；`DatabaseInitializer` 幂等对齐 7 个内建（缺失→插入、已存在非内建→`MarkAsBuiltIn`）。
3. **引用计数**：`IAgentRepository.CountByRoleAsync(tenantId, roleCode)` + `AgentRoleSummary.AgentCount`，列表展示「被引用 Agent 数」。
4. **编辑端点**：新增 `PUT /api/v1/agent-roles/{roleCode}` + `UpdateAgentRoleDefinitionCommand/Handler`（内建 `RoleCode` 锁、不可删）。
5. **删除拦截**：`DeleteAgentRoleCommand` 重写为 `AgentRoleDeletionOutcome` 枚举（Deleted/NotFound/BuiltInConflict/InUseConflict）；内建→409、被引用→409、不存在→404、可用→204。
6. **前端收口**：`AgentRolesPage` 删硬编码 `BUILT_IN_ROLES`、按 `IsBuiltIn` 分区、新建/编辑/删除模态 + RBAC + `agentCount` 展示；`AgentsPage` 默认 `roleCode` 改为 `development`；`types`/`api`/`locales` 对齐（含 i18n 对称测试 4 例，zh-CN 去字面 "Agent"）。

## 2. Code Review（ddd-code-reviewer）结论
**穷尽分析无遗留缺陷**。核心探查点：
- **控制流**：`Controller → IMediator.Send → Handler` 全链路；所有 Handler 经 `RegisterServicesFromAssembly` 自动注册；`UnitOfWorkBehavior` 对 `ICommand` 自动 `SaveChangesAsync`（Update/Delete 持久化经 `PUT` 集成测试验真）。
- **EF 迁移**：`AddAgentRoleIsBuiltIn` 设 `defaultValue:false`，存量行不会因新非空列而报错。
- **静默崩溃路径**：`UpdateAgentRoleDefinitionCommandHandler` 查无→`null`→Controller 映射 404；`DeleteAgentRoleCommandHandler` 查无→NotFound、内建→BuiltInConflict；`CreateAgentCommandHandler` `FromCode` 兜底造 `AgentType` 不崩。

### 审查发现与修复（已当场修复，非遗留）
| Severity | 文件:行 | 发现 | 修复 |
|----------|---------|------|------|
| P2 | `AgentsController.cs:60` | 新建 Agent 默认 `RoleCode ?? "developer"`，`"developer"` 已不在新目录 → 造游离 `AgentType` | 改为 `"development"` |
| P2 | `DatabaseInitializer.cs` | 设计 §3.1 错误假设存量 Agent code 已与新目录一致；实际旧 code（architect/developer/tester/pm/tech-writer）整体不符 → 存量 Agent 游离 | 新增 legacy→new 幂等映射（`IgnoreQueryFilters()` 全租户扫描） |
| P3 | `AgentsPage.contract.test.tsx:15` | 契约测试 fixture 用旧 `'developer'` | 改为 `'development'` + 断言同步 |
| P3 | `AgentType.cs` | XML 示例 `"developer"/"architect"` 过时 | 改为 `"development"/"architecture"` |

### 未修观察（超范围 / 既有设计，非回归）
- **P3-OBS-1**：`NegotiationOrchestrator`/`SequentialOrchestrator`/`RoleBasedSelectionStrategy` 仍按 `WorkflowStep.StepName` 子串派发 `architecture`/`code` 等**步骤角色码**，属工作流步骤分类，非 Agent 角色目录，需单独设计决策，留作后续。
- **P3-OBS-2**：`AgentRoleDefinition` 非租户隔离，自定义角色全局可见 —— 既有设计，F19 不改租户隔离（仅内建共享为预期）。

### Top 3 运行时风险
1. 存量 Agent 旧 `RoleCode` 游离（**已修复** via remap）—— `DatabaseInitializer`。
2. 自定义角色跨租户可见（P3-OBS-2，既有设计，无崩溃风险）。
3. 编排器步骤角色码非目录码（P3-OBS-1，仅影响工作流步骤归类显示，不影响 Agent 角色目录）。

## 3. Structure Gate（ddd-phase-quality-gate）结论
12 类全扫 PASS（G1–G12，详见 `features/agent-roles-builtin.md` §7）。无 DI 缺口、无 DDD 层违规、无 EF 映射缺口、无缺 ct / internal sealed / null 守卫；API 基础设施（鉴权 401/403、409/404/204 冲突处理）到位；无蓝图漂移、无缺 XML 注释、无 Swagger 遗漏、无死代码。`AgentRoleDefinition` 非租户隔离为设计项，非缺陷。

## 4. Codebase Optimizer（codebase-optimizer）结论
七维聚焦 F19 改动范围，0 open：
- **无桩代码**：Handler 真实持久化/查询；Controller 真实派发；仓储真实查库（`CountByRoleAsync`/`GetByRoleAsync`）。
- **无 XSS**：前端 `AgentRolesPage` 仅 antd 声明式组件 + `t()` 文案，无 `dangerouslySetInnerHTML`。
- **前端类型显式无 any**：`tsc --noEmit` 0 error。
- **无未捕获 Promise**：`useApiState` 统一处理；模态/删除 `Popconfirm` 走 async/await。
- **无未用导入**：`vite build` 0 警告。
- **无硬编码密钥**：role codes 来自 `BuiltInRoleCatalog` 常量与 DB。
- **后端**：`dotnet build` 0/0；全方案 `dotnet test` **287/287**（SpecFlow 41 / Arch 9 / App 103 / Api 27 / Integration 5 / Infra 102，含 F19 新增 parity 3 + handler 7 + Api 集成 7）。
- **前端**：`tsc 0` + `vitest` **38/38** + `vite build` 通过。
- **已知环境限制**：`qa.mjs` 的 `lint` 闸门在本仓库环境**恒失败** —— `eslint.config.js` 引用 `@eslint/js`/`typescript-eslint`/`eslint-plugin-react-hooks`/`eslint-plugin-react-refresh`/`globals`/`eslint`，但 `package.json` devDependencies **未声明**这些包（orphaned config）。此为非 F19 的仓库级依赖缺口；typecheck/build/unit 三道实质闸门全绿，lint 不阻塞本次交付，建议独立 PR 补 `package.json` 依赖。

## 5. 验证汇总
| 层 | 命令 | 结果 |
|----|------|------|
| 后端构建 | `dotnet build` | 0 error / 0 warning |
| 后端测试 | `dotnet test src/AgentPlatform.sln` | **287/287** |
| 前端类型 | `tsc --noEmit` | 0 error |
| 前端单测 | `vitest run` | **38/38** |
| 前端构建 | `vite build` | 通过 |
| 质量门 | 三道（reviewer / structureGate / optimizer） | 全部 PASS（cleared:true） |

## 6. 遗留 / 后续
- `package.json` 补全 eslint 依赖（修 orphaned `eslint.config.js`），使 `qa.mjs` lint 闸门可跑。
- 编排器步骤角色码（P3-OBS-1）与自定义角色租户隔离（P3-OBS-2）为独立议题，不在 F19。
