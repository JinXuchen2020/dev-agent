# F17 · AgentConfiguration 实例化联动（方案 A 细化）

> 状态：`open`（设计就绪，待实现）
> 优先级：`[P2]`（与 F16 卡片化强耦合，建议 F16 先行或本 feature 内一并卡片化）
> 风险：`🟡 中风险`（前端 CRUD 补全 + 1 个新端点 + 契约小改 + RBAC 收敛；不触 EF 迁移）
> 来源：2026-07-27 对 `AgentConfigurationsPage` 的分析——判定该聚合为「版本化 YAML 定义库孤岛」，当前既不被新建 Agent 消费、也无前端增删改 UI、且与「我的凭据」页重复 tab。本 feature 落地前一轮给出的**方案 A**：保留并补全，使之成为真正的「Agent 定义/模板库」，与 operational 的 `Agents` CRUD 形成「定义 vs 实例」分工。

---

## 1 · 目标

让 `AgentConfiguration`（版本化 YAML 定义）**真正被消费**：

1. 用户可在 `AgentConfigurationsPage` 对定义做**完整 CRUD**（当前后端已就绪，前端只有只读 View，缺 Create/Edit/Delete UI）。
2. 用户在 `AgentsPage` 可**「基于模板新建」**——选一个 Configuration → 系统用其 YAML 预填 Agent 创建表单 → 用户确认/微调 → 落一个 `Agent`。
3. **消除重复与 RBAC 不一致**：`AgentConfigurationsPage` 内的「凭据设置」tab 与独立页「我的凭据」(`CredentialSettingsPage`, 路由 `/credentials`) 完全同源同功能 → 删除该 tab，凭据统一走「我的凭据」；并把 Configurations 菜单项收敛为 `Admin` 可见（与后端 `[Authorize(Roles="Admin")]` 对齐），消除非 Admin 用户见报错入口的问题。

---

## 2 · 现状核验（已读真实代码）

- `AgentConfiguration` 聚合：`src/AgentPlatform.Domain/Aggregates/AgentConfigurations/AgentConfiguration.cs` —— 含 `YamlContent` + `ConfigurationVersion`(语义化版本, `VersionBump` Major/Minor/Patch) + `Status`(Draft/Active/Archived 状态机 `Activate()/Archive()`) + `AgentTypeCode`(可选归类)。
- 后端 CRUD 已完整：`AgentConfigurationsController.cs` —— `POST`(Create, Admin) / `PUT {id}`(Update, Admin) / `DELETE {id}`(Admin) / `GET`(List) / `GET {id}` / `GET by-type/{agentTypeCode}`。**仅缺「实例化 Agent」消费端**。
- `CreateAgentCommand.cs:16` 字段：`Name/RoleCode/ModelProvider/ModelName/ModelApiUrl/SystemPrompt/TenantId` —— **无 `ConfigurationId`**，创建 Agent 不引用定义。
- 全仓 `AgentConfigurationId` 引用 = **0**；前端 grep `byType`/`AgentTypeCode`/`GetConfigurationsByType` = **0** → 定义库与运行时（对话/工作流）及 Agent 创建表单**完全断裂**。
- `AgentConfigurationsPage.tsx:37` Action 列仅 `View`；抽屉仅 `Descriptions` + `yamlContent`(`AgentConfigurationsPage.tsx:142-169`)；第 2 个 tab 内嵌 `CredentialManager`(`AgentConfigurationsPage.tsx:90` 一带) 与「我的凭据」重复。
- `AppLayout.tsx:42` 的 Configurations 菜单项**无 RBAC 门控**（所有登录用户可见），但其中凭据 tab 对非 Admin/Operator 实际 403。

---

## 3 · 架构与改造

### 3.1 后端（最小契约改动）

**新增端点 `GET /api/v1/agent-configurations/{id}/template`**

- 门禁 `[Authorize(Roles="Admin")]`；tenant-scoped（仓储 `IAgentConfigurationRepository` 已实现 `ITenantScoped` 查询过滤）。
- Handler `GetConfigurationTemplateQueryHandler`：加载 `AgentConfiguration`（按 `(id, tenantId)`），解析 `YamlContent`（YAML → 结构化字段），返回 `ConfigurationAgentTemplate` DTO：
  ```csharp
  public sealed record ConfigurationAgentTemplate(
      string Name,
      string? Description,
      string? RoleCode,        // 映射自 YAML 的 agent.role / agentTypeCode
      string? ModelProvider,
      string? ModelName,
      string? ModelApiUrl,
      string SystemPrompt,
      string SourceVersion);   // 例如 "1.2.0"，供前端展示「源自 v1.2.0」
  ```
- YAML 解析：新增 `Infrastructure/Yaml/AgentConfigurationYamlParser.cs`，用 `YamlDotNet`（`AgentPlatform.Infrastructure` 已引）反序列化到内部 `AgentYamlModel { Name?, Role?, Model { Provider, Name, ApiUrl }, SystemPrompt }`。**容错**：缺字段→留 null（前端以表单默认值兜底）；解析失败→400 中文原因。
- 为何服务端解析而非前端 `js-yaml`：YAML 解析单点（服务端），前端只消费结构化结果；避免重复依赖与解析漂移。

**（可选，v1 不强制）`CreateAgentCommand` 加 `Guid? ConfigurationId` 溯源字段**

- Handler `CreateAgentCommandHandler`：若 `ConfigurationId` 有值，加载定义（tenant-scoped）写一条审计/溯源（`Agent` 聚合可加 `OriginConfigurationId` 导航/标量，属后续；v1 仅记审计日志即可，**不强制改 `Agent` 聚合**）。**v1 原则**：实例化逻辑以前端「预填表单」为主，后端 `ConfigurationId` 仅作溯源/审计，不影响落库结构（无 EF 迁移）。

### 3.2 前端

**`AgentConfigurationsPage.tsx` —— 补 CRUD + 去重 tab**

- 移除第 2 个「凭据设置」tab（凭据统一走「我的凭据」路由 `/credentials`）。
- 列表上方加「新建定义」按钮 → `Modal` 表单（`name` / `description` / `agentTypeCode` Select 取 `getAgentRoles()` / `yamlContent` `Input.TextArea` 代码框，语法高亮可选）。
- 每行操作 `Dropdown`(⋯) 收纳「编辑 / 删除」：编辑复用同 Modal（先 `GET /api/v1/agent-configurations/{id}` 回填）；删除 `Popconfirm` 调 `deleteAgentConfiguration` 后刷新。
- （与 F16 协同）列表渲染若 F16 已落地则复用 `EntityCardGrid`，否则本 feature 内以卡片/表格二选一，避免与 F16 冲突（见 §5 D2）。

**`AgentsPage.tsx` —— 加「基于模板新建」入口**

- 现有「新建 Agent」旁加「基于模板新建」按钮 → 打开 `Modal`/`Drawer`：调 `getAgentConfigurations()` 列出定义（`status=Active` 优先），用户选其一 → 调 `getAgentConfigurationTemplate(id)` → 把返回字段写入现有 `AgentsPage` 创建 `Form`（`name/roleCode/modelProvider/modelName/modelApiUrl/systemPrompt`，保留 `status=Active` 默认值）→ 用户可微调 → `createAgent`（如后端加 `configurationId` 溯源则一并带上）。
- 命名建议：「基于模板新建」入口与「新建 Agent」并列，视觉区分（如副按钮/Dropdown 分裂按钮）。

**`src/services/api.ts` 补齐**

- `getAgentConfigurations(params?)` —— 已存在（确认签名 `api.get('/agent-configurations',{params})`）。
- 新增：
  - `getAgentConfigurationTemplate(id)` → `api.get<ConfigurationAgentTemplate>('/agent-configurations/${id}/template')`
  - `createAgentConfiguration(req: CreateAgentConfigurationRequest)` → `api.post('/agent-configurations', req)`
  - `updateAgentConfiguration(id, req)` → `api.put('/agent-configurations/${id}', req)`
  - `deleteAgentConfiguration(id)` → `api.delete('/agent-configurations/${id}')`
- `types/index.ts` 新增 `CreateAgentConfigurationRequest` / `UpdateAgentConfigurationRequest` / `ConfigurationAgentTemplate`（与后端 DTO 对齐）。

**`AppLayout.tsx` —— RBAC 收敛**

- Configurations 菜单项加 `Admin`-only 可见（`isAdmin` 来自 `useAppStore`，与 Agents 页同款 case-insensitive 判定），与后端 `[Authorize(Roles="Admin")]` 对齐；非 Admin 不再看到会报错的入口。

---

## 4 · 验收子项

- **后端**
  - `GET /agent-configurations/{id}/template`：返回结构化 `ConfigurationAgentTemplate`；YAML 缺字段→null 兜底；非法 YAML→400 中文原因；跨租户 id→404（tenant 过滤）；非 Admin→403。
  - （若实现溯源）`CreateAgentCommand` 带 `ConfigurationId` → 审计日志含溯源；不带→行为不变。
  - 单测：`AgentConfigurationYamlParser` 解析正常/缺失字段/非法 YAML 三例；`GetConfigurationTemplateQueryHandler` tenant 过滤 + 映射正确。
- **前端**
  - `AgentConfigurationsPage`：可新建/编辑/删除定义；「凭据设置」tab 已移除；列表渲染与 F16 一致（卡片或表格，不冲突）。
  - `AgentsPage`：「基于模板新建」→ 选定义 → 表单预填正确 → 创建出 Agent；预填值可改。
  - `AppLayout`：Configurations 仅 Admin 可见。
  - e2e（Python UTF-8）：Admin 登录 → 新建定义(含 YAML) → GET 列表含 → 「基于模板新建」选它 → 表单预填 → 改 name 创建 → `getAgents` 含新 Agent（字段与 YAML 一致）→ 删定义 → 删 Agent 清理。
- **质量门**：build 0/0、`dotnet test` 全绿（含 parser/handler 单测，既有 244+ 不回归）、前端 tsc 0 + vitest 全过 + vite build；`.quality-gate.json` 追加 notes 保 `cleared:true`。

---

## 5 · 决策（已锁定 2026-07-29）

- **D1 实例化方式**：前端「预填表单」为主、后端 `ConfigurationId` 仅溯源（v1 不强制改 `Agent` 聚合、无 EF 迁移）。备选：后端新增 `POST /agent-configurations/{id}/instantiate` 直接落 Agent（更重，留待 v2）。✅ 已锁定
- **D2 与 F16 时序**：F17 的 `AgentConfigurationsPage` 列表渲染须与 F16 卡片化一致——**建议 F16 先行**，F17 在其卡片 UI 上叠加 CRUD + 模板入口；若 F16 未做，F17 自行以卡片呈现并标记，F16 后续跳过该页。✅ 已锁定（F16 已于 2026-07-29 经 PR #12 并入 master，F17 从 master 派生，直接复用 EntityCardGrid 卡片 UI）
- **D3 YAML 编辑器**：v1 用 `Input.TextArea`（等宽字体 + 行号可选），不引入 Monaco/CodeMirror（重依赖）；语法高亮列已知残留。✅ 已锁定
- **D4 模板字段映射约定**：YAML 采纳约定结构（详见 §3.1 `AgentYamlModel`），文档化在 `AgentConfiguration.cs` 或 README；前端预填以「后端 template 端点返回」为准，不自行解析 YAML。✅ 已锁定

---

## 6 · 风险与缓解

- 🟡 中风险：前端 CRUD 跨模态 + 1 新端点 + `AppLayout` RBAC 改动。
  - 缓解：后端 CRUD 已就绪仅差 UI，工作量集中在前端；新端点仅读 + 解析，无写副作用（除可选溯源审计），不触迁移。
  - YAML 解析漂移：单点服务端解析 + 单测覆盖；前端不碰 YAML 反序列化。
  - 与 F16 渲染冲突：见 D2，明确单一 owner 页。
- 非风险：不改 `Agent`/`AgentConfiguration` 聚合存储结构；不引入新第三方（YAML 已有 `YamlDotNet`）。
