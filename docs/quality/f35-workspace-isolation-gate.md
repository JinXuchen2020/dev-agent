# F35 · 多工作空间隔离（Workspace）质量门报告

> 日期：2026-08-31 · 分支 `feat/f35-workspace-isolation` · feature-builder 全栈流水线
> 设计文档：`features/f35-workspace-isolation.md`（§6 决策 D1–D5 用户锁定 2026-08-31）

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer | **PASS**（0 open） | 2×P1 + 2×P2 + 1×P3 全部修复 |
| ddd-phase-quality-gate | **PASS**（P0=0 P1=0 P2=0；P3 1 修复 + 2 waiver） | checklist 已嵌入设计文档 §Quality Gate Checklist |
| codebase-optimizer | **PASS**（Round F35-01，0 open；1 修复 + 5 waiver） | 记录见 `.codebase-optimizer/rounds/round-f35-01-*` |

## ddd-code-reviewer 修复记录

| 严重度 | 文件 | 问题 | 修复 |
|---|---|---|---|
| P1 安全 | `WorkspaceProvider.cs` / 新增 `Api/Middleware/WorkspaceHeaderGuardMiddleware.cs` | `X-Workspace-Id` 头无可见性校验，非 Admin 伪造头可读同租户任意工作空间，绕过 D3=B 成员可见性 | 新中间件（UseAuthorization 后）：非 Admin 且头 ≠ claim 时校验默认/成员可见性，不可见即剥离头回退 claim；Admin 亦校验头 id 属于本租户（结构门补强） |
| P1 回归 | `TriggerWorkflowCommandHandler.cs` | scope 的 DbContext 过滤器构造期冻结为「默认租户×默认工作空间」，非默认工作空间的工作流被触发器静默跳过（master 会触发 → F35 回归） | 新增 `IWorkflowRepository.GetByIdForTriggerAsync`（IgnoreQueryFilters + 显式 TenantId 守卫），触发路径改用；2 处单测 stub 同步 |
| P2 | `WorkspaceProvider.cs` | claim/header 为 `Guid.Empty` 被当合法空间 → 全站空集不回退 | 空 Guid 视为缺省沿解析链回退 |
| P2 | `WorkspaceSwitcher.tsx` | 删除「当前」空间后 cookie 旧 claim 失效 → 全站查询为空 | 删除当前空间后自动 switch 回默认空间（清空 currentWorkspaceId） |
| P3 | `WorkspacesController.cs` | API-Key 主体无 Name/Email claim，重签 JWT 抛异常 → 500 | 回退空串 |

## 结构门（12 类 audit）要点

DI 注册完备（6 新接口 + 中间件）；DDD 分层零违规；EF 迁移/快照一致（2 新表 + 21 列 + 唯一索引 (TenantId,Name) / (WorkspaceId,UserId)）；CancellationToken 全覆盖；新类全 `internal sealed`；enum/方法零死代码；`dotnet list package --vulnerable` 无。Waiver：① `WorkspaceDirectory` 单例 ConcurrentDictionary 只增不减（键数=租户数，有界 <1MB；无删租户功能故无清理路径；目标=未来租户生命周期 feature）；② `WorkspaceProvisioner` 的 `"Default"` 契约常量（设计 §1 产品语义，同 `DefaultTenantIdSeed` 模式）。

## codebase-optimizer（Round F35-01，scope=F35 diff 45 修改 + 31 新增）

修复：存储键魔法串单源化（`WORKSPACE_STORAGE_KEY` 常量落 `api.ts`，`appStore` 导入，2 个测试 vi.mock 修补）。Waiver（5 项 P3）：无组合索引（租户索引主导选择性）、启动回填有界扫描、bootstrapAuth 陈旧键自愈（服务端剥离兜底）、触发 override 时序（`GetByIdForTriggerAsync` 已补偿）、迁移命名/注释语言混杂（项目既有风格）。

## 验证

- 后端：`dotnet build AgentPlatform.sln` 0 警告 0 错误；`dotnet test`：Application **238/238**、Infrastructure **158 通过 + 6 跳过**（Docker 门控）、Api **35/35**、Architecture **9/9**、Integration **5/5**（需 `OPENAI__Key` 环境变量）、SpecFlow **114/115**（唯一失败 = master 既有 LLM 用例「Admin 创建会话后向其发送消息得到回复」，已验证 master 同样失败，豁免）。
- 新增测试：Application handler 12 例（创建冲突/删除守卫 4 结局/可见性/switch 成员校验/成员添加）+ Infrastructure EF 隔离 4 例（SaveChanges 注入、组合过滤器按空间隔离、Workspace/Member 不叠加空间过滤、跨空间计数）。
- 前端：`tsc --noEmit` 0 error；vitest 42/44（2 个失败均为 master 既有：i18n「搭建 Agent 团队」、AgentsPage contract，豁免）；`vite build` 通过；`bddgen` 绑定校验通过（E2E 由 CI 驱动，本地不跑）。
- 模型一致性：后端 camelCase DTO（`WorkspaceDto{id,name,description,isDefault,createdAt}`、`WorkspaceMemberDto{userId,email,joinedAt}`、`AuthUserDto.currentWorkspaceId?`、switch 响应 `{workspace,token}`）与前端 `types/index.ts` 逐字段对齐；`tsc` 通过。

## 已知残留（非阻断）

1. 触发/调度执行 v1 仅落租户默认工作空间（设计 §已知限制；F30/F37 durable/队列化时随 workspace 上下文传递深化）。
2. 成员列表 N+1（`ListWorkspaceMembersQueryHandler` 逐成员查用户；成员数量小，可接受）。
3. workspace 名称唯一性大小写语义依赖 DB collation（SQLite BINARY 区分大小写）。
4. `AuditLog`/`ExecutionLog`/`AgentRunRecord` 运行期 `WorkspaceId` 恒空（D2=A 仅补列预留，不叠加过滤）。
