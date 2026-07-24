# F3 · 页面交互打磨（Page Interaction Polish）

> 史诗 id：F3　|　优先级：P2　|　类型：前端为主；含一处经用户授权的后端契约扩展（`GET /conversations` 增 `status` + `q` 过滤）
> 分支：`feat/f3-page-polish`
> 来源：`features/backlog.md` F3 史诗（B9 / B10 / B11 / Conversations 搜索筛选 / O12 / O13）

## 1. 目标

让列表 / 筛选 / 表单类页面的交互**正确且一致**，消除「状态色块错乱、空操作无反馈、假分页、卸载后 setState、无请求取消」等打磨类缺陷。纯前端，不改动任何后端契约。

## 2. 范围与边界（硬约束）

- **前端为主 + 一处后端扩展**：后端 `agent-configurations` / `workflows` / `execution-logs` 三个列表端点**已支持** `skip` / `take` / `totalCount`。`Conversations` 端点原返回全量数组（无筛选参数）；按用户明确授权，F3 已**扩展** `GET /api/v1/conversations` 支持 `?status`(整数枚举) + `?q`(自由文本) 过滤，并把 Conversations 页搜索/筛选切到**服务端**。这是本 feature 唯一一处后端契约变更（用户授权，不违反「先问人」红线）。
- **枚举序列化事实（B10 根因）**：`Program.cs` 仅配置 `JsonNamingPolicy.CamelCase`，**未注册 `JsonStringEnumConverter`**，故所有枚举按**整数**序列化。实测枚举序：
  - `WorkflowState`：Pending=0 / Running=1 / Paused=2 / Completed=3 / Failed=4 / RolledBack=5
  - `ConversationStatus`：Active=0 / Closed=1 / Archived=2
  - 因此前端现有 `statusColors` 用小写字符串做 key 永远 miss → 全部回落 `default` 色块，且筛选下拉值是小写字符串（靠模型绑定大小写不敏感才生效，脆弱）。修复一律在**前端建状态映射表**，不碰后端序列化（避免契约破坏性改动）。
- 不借机重构无关代码；不引入新的路由或鉴权结构。

## 3. 后端契约现状（复用，不改）

| 端点 | 分页/筛选参数（已支持） | 返回 |
| --- | --- | --- |
| `GET /api/v1/agent-configurations` | `?type&skip&take` | `{ items: AgentConfiguration[]; totalCount: number }`（`yamlContent` 已在 `items` 中返回） |
| `GET /api/v1/workflows` | `?status&skip&take` | `{ items: Workflow[]; totalCount: number }`（`currentState` 为整数枚举） |
| `GET /api/v1/execution-logs` | `?status&from&to&skip&take` | `{ items: ExecutionLog[]; totalCount: number }`（`status` 为整数枚举） |
| `GET /api/v1/conversations` | `?status`(整数枚举) + `?q`(自由文本，匹配 id/workflowId/knowledgeBaseId/collectionName/消息正文) | `Conversation[]`（`status` 为整数枚举） |

`runWorkflow({ name, initialContext })` → `POST /api/v1/workflows`（创建并运行工作流）。

## 4. 前端数据模型与状态映射（新增/调整）

### 4.1 新增 `src/status.ts`（单一事实源）
- `WORKFLOW_STATE_META: Record<number, { label; color }>` —— 整数枚举 → 标签 + antd Tag 色 token。
- `mapWorkflowStatus(state: string | number | null | undefined): { label; color }` —— 同时兼容整数与字符串（防御性），兜底 `default`。
- `WORKFLOW_STATUS_FILTER_OPTIONS` —— 筛选下拉选项，`value` 取**整数枚举值**（模型绑定按名/按整数均可，整数最无歧义），不裸传字面量。
- `CONVERSATION_STATUS_META: Record<number, { label; tone }>` —— 整数枚举 → 中文标签 + `StatusBadge` tone。

### 4.2 类型微调（`src/types/index.ts`）
- `ExecutionLog.status` / `Workflow.currentState` 保持 `string`（运行期实为数字，映射函数接受 `string|number`，无类型错误）；
- `api.ts` 中 `getExecutionLogs({ status })` 的 `status` 参数类型放宽为 `string | number`，以接纳整数枚举值；其余列表 getter 增加可选 `signal?: AbortSignal`。

## 5. 验收子项实现方案

### B9 · AgentConfigurations YAML 详情抽屉
- `AgentConfigurationsPage` 增加 antd `Drawer`；行点击（或「View」操作）打开，展示 `name / agentType / version / isActive / createdAt` + `yamlContent`（`<pre>` 等宽字体、可横向滚动，不做第三方语法高亮，避免引入新依赖）。
- 表格 `rowKey="id"`，新增操作列 `View` 按钮，避免误触整行跳转。

### B10 · 状态筛选枚举映射（ExecutionLogs + Workflows）
- 两页状态 `Tag` 统一改用 `mapWorkflowStatus(...).color` + `.label`，修复错乱色块。
- ExecutionLogs 状态筛选 `Select` 选项改用 `WORKFLOW_STATUS_FILTER_OPTIONS`（value=整数），`onChange` 传整数给 `getExecutionLogs({ status })`。
- Workflows 页补「按状态筛选」下拉（与 ExecutionLogs 同源映射），对齐 F3「列表/筛选一致」目标。

### B11 · Workflows「快速运行」错误处理
- `handleRun`：空名 → `message.warning('请输入工作流名称')` 并保持弹窗打开，不静默返回。
- `runWorkflow` 包 `try/catch`；成功 → 关弹窗 + 清空 + 刷新列表；失败 → `message.error(getErrorMessage(e))`，弹窗保持打开便于重试。

### Conversations 搜索 / 状态筛选（服务端，对齐 Agents 页）
- 顶部加 `Input.Search`（回车触发）+ 状态 `Select`（`CONVERSATION_STATUS_META`，含「全部」），二者均作为查询参数传给 `GET /api/v1/conversations?status=&q=`，由后端在租户内过滤。
- `getConversations({ status, q, signal })` 透传参数；`ConversationsPage` 的 `useEffect` 依赖 `[appliedQ, statusFilter]` 重新拉取，移除原客户端 `filtered` 内存过滤。
- `q` 后端语义（handler 内 `StringComparison.OrdinalIgnoreCase`）：`Id` / `WorkflowId` / `KnowledgeBaseId` / `CollectionName` / `Messages[].Content` 任一包含关键字即命中。
- 状态列改用 `CONVERSATION_STATUS_META` 映射中文标签传给 `StatusBadge`，修复数字显示。

### O12 · 列表服务端分页（ExecutionLogs / Workflows / AgentConfigurations）
- 三页 antd `Table` 的 `pagination` 接 `total` + `current` + `pageSize`，`onChange` 计算 `skip=(current-1)*pageSize` / `take=pageSize` 后重新拉取 `items` + `totalCount`，消除「前端假分页与后端 totalCount 不一致」。
- Conversations 保持客户端分页（后端返回全量数组，无 totalCount）。

### O13 · 请求取消与卸载安全（AbortController）
- 四个列表 getter 增加 `signal?: AbortSignal` 参数并透传给 axios。
- 各页 `useEffect` 内建 `AbortController`，`finally` 外增加 `cleanup` 中 `controller.abort()`，杜绝卸载后 `setState` 与重复请求。

## 6. 质量门禁清单（ddd-phase-quality-gate 嵌入项）

- **P0（阻断）**
  - [ ] 所有新增/修改页面 `tsc --noEmit` 通过，0 类型错误、0 `any`。
  - [ ] 列表服务端分页 `skip/take/totalCount` 与后端契约字段名一致。
  - [ ] 状态映射覆盖全部枚举值（含兜底），无未处理枚举导致空白色块。
- **P1（高）**
  - [ ] Quick Run 空名有 `warning`、失败有 `error` toast，且弹窗状态正确。
  - [ ] 卸载 `AbortController.abort()` 生效，无 setState-after-unmount 警告。
  - [ ] YAML 详情抽屉正确渲染 `yamlContent`，不溢出布局。
- **P2（中）**
  - [ ] Conversations 搜索/筛选已切到服务端（`/conversations?status&q`），过滤逻辑正确。
  - [ ] 筛选/分页切换不触发整页刷新或重复副作用。
  - [ ] 复用既有设计令牌（`colors` / `Card` / `PageHeader` / `StatusBadge`），无新硬编码色值。
- **P3（低）**
  - [ ] 无死代码、无未用导入；lint（eslint）净。
  - [ ] 后端仅 `GetConversations` 查询/处理器/控制器三处增量改动，无破坏性契约变更。

## 7. 风险与回归

- **回归面**：ExecutionLogs / Workflows 的筛选值与分页参数格式变化（由小写字符串→整数枚举）。后端 `WorkflowState? status` 模型绑定对整数与大小写不敏感的名均接受，故筛选行为不变甚至更稳。
- **无后端改动**：不触发 pre-commit 之外的任何后端质量门；`.quality-gate.json` 随本 feature 前端改动一起提交。
- **e2e**：新增交互建议补独立 spec（Conversations 搜索/筛选、Workflows 空名警告、ExecutionLogs 分页）；依赖本地后端可用时由 `scripts/qa.mjs --e2e` 执行。
