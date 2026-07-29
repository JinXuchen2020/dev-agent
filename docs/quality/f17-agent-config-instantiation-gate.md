# F17 · AgentConfiguration 实例化联动 — 质量门报告

> 阶段：`f17-agent-config-instantiation`
> 日期：2026-07-29
> 门结果：**PASS（cleared: true）**

## 1 · 范围与改动清单

把 `AgentConfiguration`（版本化 YAML 定义库孤岛）变为真正被消费的「Agent 定义/模板库」。

**后端（无 EF 迁移）**
- 新增 `GET /api/v1/agent-configurations/{id}/template` `[Authorize(Roles="Admin")]`
  - `AgentConfigurationManagement/Queries/GetConfigurationTemplate/GetConfigurationTemplateQuery.cs`
  - `AgentConfigurationResponse.cs` 新增 `ConfigurationAgentTemplate` DTO
  - 复用既有 `IYamlConfigurationParser`（设计文档原拟新增解析类，经核验已存在，改为复用）
- D1 溯源：`CreateAgentCommand` 新增可选 `Guid? ConfigurationId`；`CreateAgentCommandHandler` 注入 `IAgentConfigurationRepository` best-effort 加载定义并把来源写入审计日志（失败不阻断创建）。

**前端**
- `AgentConfigurationsPage.tsx`：补全完整 CRUD（新建/编辑 Modal + 每行 ⋯ 编辑 / Popconfirm 删除，均 Admin 门控）；移除与「我的凭据」重复的凭据 tab；抽屉明细改为拉 `GET {id}` 详情取 `yamlContent`（列表 summary 不含 yamlContent）。
- `AgentsPage.tsx`：新增「基于模板新建」——弹窗列定义（Active 优先）→ 选其一 → `getAgentConfigurationTemplate` 结构化预填创建表单 → `createAgent` 透传 `configurationId`；模板模型不在目录时注入合成目录项避免静默丢 provider。
- `services/api.ts` 补齐 5 个方法；`types/index.ts` 修复 `AgentConfiguration` 漂移（`agentTypeCode`/`status`/`updatedAt`）并新增 3 个类型。
- `layouts/AppLayout.tsx`：Configurations 菜单收敛为 Admin 仅见。
- `locales/zh-CN.ts` + `en-US.ts`：镜像新增 F17 文案（对称性测试通过）。

## 2 · 三道质量门

| 门 | 结果 | 关键结论 |
|----|------|----------|
| ddd-code-reviewer | PASS（0 open） | 模板端点 tenant 显式比对过滤（无跨租户泄漏）；YAML 容错解析兼容嵌套 `Dictionary<object,object?>`；D1 溯源不阻断创建；前端类型漂移已对齐；修复模板模型不在目录的静默丢 provider（注入合成目录项） |
| ddd-phase-quality-gate | PASS（0 open） | 后端新增端点/DTO/Handler + 可选溯源字段；前端 CRUD + 去重 tab + 模板预填；api/types/AppLayout/i18n 一致性逐项核验 |
| codebase-optimizer | PASS（0 open） | 无 any / XSS / 未捕获 Promise；strict tsc 0 error；无桩代码；无未用导入 |

## 3 · 验证矩阵

| 项 | 命令/范围 | 结果 |
|----|-----------|------|
| `dotnet build`（Api） | `dotnet build src/AgentPlatform.Api/...` | 0 error / 0 warning |
| 后端全方案单测 | `dotnet test src/AgentPlatform.sln` | **260/260, 0 fail**（SpecFlow 41 / Architecture 6 / Application 90 / Infrastructure 102 / Api 16 / Integration 5） |
| 前端类型 | `tsc --noEmit` | 0 error |
| 前端单测 | `vitest run` | 38/38（11 文件，含 i18n 对称 4 项） |
| 前端构建 | `vite build` | 通过 |

### 3.1 · 附带修正：Infrastructure.Tests 飘红测试（非 F17 范围）

全方案 `dotnet test` 首跑暴露 `AgentPlatform.Infrastructure.Tests.Security.AesGcmEncryptorTests.Decrypt_ThrowsOnTamperedCiphertext` 偶发失败（约 1/16 概率）：
原 hex-flip `(char)(c ^ 1)` 在中间字符为 `'a'` 时得到 `` ` ``（非法 hex），`Convert.FromHexString` 抛 `FormatException` 而非预期的 `AuthenticationTagMismatchException`。
该测试与 F17 改动零交集（Infrastructure 项目无 F17 改动），属**预存飘红**；为兑现「全方案绿」的诚实门结论，已将其 hex 翻转改为全字符确定性映射到合法异值字符（仍必触发 GCM 标签失配）。修正后 5 次连跑稳定 6/6，`Infrastructure.Tests` 由 101+1fail → 102/102。

## 4 · 设计偏离说明

- 设计文档 §3.1 拟「新增 `Infrastructure/Yaml/AgentConfigurationYamlParser.cs`」。经核验 `IYamlConfigurationParser`（`Application/Abstractions` + `Infrastructure/Configuration/YamlConfigurationParserService`）已存在且为 YamlDotNet 实现，故改为**复用**，不新增解析类——单点服务端解析 + 单测覆盖不变，避免重复依赖。
- YAML 约定字段（文档化于 `ConfigurationAgentTemplate` XML 注释与后端序列化）：`agent_role` / `system_prompt` / `model.{provider,name,api_url}`。

## 5 · 残留/已知限制（非门阻塞）

- 模板模型 prefill 仅在命中平台模型目录或注入合成项时带回 provider；若目录中 provider 名与模板不一致，仍由用户在下拉中校正（v1 预期行为）。
- 非 Admin 用户隐藏 Configurations 菜单，但 `GET /agent-configurations` 后端为任意已认证用户可读；直接路由仍可只读访问（与后端 RBAC 一致，非安全缺口）。

## 6 · 提交

- 分支：`feat/f17-agent-config-instantiation`（自 master 派生，F16 已并入）
- 提交：`Quality-Gate: f17-agent-config-instantiation PASS` —— 仅本地提交，**未 push**
