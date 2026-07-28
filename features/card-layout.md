# F16 · 列表统一改为卡片（Card）形式展示  [P2]

> 设计文档（features/ 设计枢纽）。本 feature 为前端 UI 打磨改造，范围 = 把所有「实体列表页」的表格渲染替换为**响应式卡片网格**。属 🟡中风险（涉及几乎所有列表页的渲染层改动，工作量大；不触后端契约，但需统一卡片组件避免各页碎片化）。
>
> 一句话：用户要求「所有页面的列表都用 card 形式展示」——把 Agents / Workflows / Conversations / KnowledgeBases / 凭据 / API Key / 执行日志 / 调研 等列表页的 `<Table>` 换成 `<Row>/<Col>` 卡片网格，每张卡显示标题 + 摘要 + 状态标签 + 操作菜单，保留搜索/筛选栏与空态/加载态。

## §1 目标
当前各列表页用 Antd `<Table columns=...>` 渲染（详见 §2 核验）。用户要求统一改为**卡片（Card）形式**展示，提升可视性与点击目标，符合现代 Agent 平台的卡片式浏览（对标 Dify/Coze 的实体卡片流）。

本 feature 交付：
- 一个**通用卡片网格组件** `EntityCardGrid`，统一列表页的卡片渲染、加载骨架、空态、响应式列数；
- 各列表页用 `EntityCardGrid` 替换 `<Table>`，每张卡由页面提供 `renderCard(item)`（标题/描述/状态 Tag/操作菜单）；
- 保留并复用现有**搜索栏 / 筛选器**（它们在表格上方，独立于渲染层）；
- 保留**空态**（`Empty`，文案延续现有「暂无…点击新建」）与**加载态**（`Skeleton` 卡片或 antd `Card loading`）；
- 交互等价：点击卡片 → 进详情/开抽屉；卡片右上角 `Dropdown`（⋯）或底部按钮提供 编辑 / 删除（`Popconfirm` 保留）。

范围**仅前端渲染层**。后端契约、DTO、接口均不变；**不触后端**。

## §2 现状核验（已 grep 真实代码，非臆测）
列表视图（`<Table>` 或 `<List>`）分布：
- **实体列表页（目标，v1 改卡片）**：
  - `AgentsPage.tsx:188` — `<Table columns={isAdmin ? [...columns, actionColumn] : columns}>`（智能体列表；含 admin 操作列）。
  - `AgentConfigurationsPage.tsx:90` — `<Table columns={columns(openDrawer)}>`（Agent 配置里的智能体表，行点击开抽屉）。
  - `WorkflowsPage.tsx:108` — `<Table columns={columns}>`（工作流列表）。
  - `ConversationsPage.tsx:153`（包在 `<Card title="会话列表">`）— `<Table columns={columns}>`（会话列表）。
  - `KnowledgeBasesPage.tsx:135`（包在 `<Card title="知识库列表">`）— `<Table columns={columns}>`（知识库列表）。
  - `CredentialManager.tsx`（F13 新增）— `<Table>`（凭据列表：名称/供应商/模型/掩码/启用/操作）。
  - `ApiKeysPage.tsx:73`（包在 `<Card title="API Key 列表">`）— `<Table columns={columns}>`（API Key 列表；该页本身 blocked，但列表渲染仍在）。
  - `ExecutionLogsPage.tsx:93` — `<Table columns={columns}>`（执行日志列表，多列 + 状态筛选 + 分页）。
  - `AgentRolesPage.tsx:39,52` — 两个 `<Card>` 各含一个 `<Table>`（角色 / 权限映射表）。
  - `ResearchPage.tsx:125` — 已用 `<List>`（调研报告列表，已接近卡片形态，v1 可保持或微调配以适应新网格）。
- **详情内子表（暂不在 v1 范围，见 §5 D2）**：`ExecutionLogDetailPage.tsx:90`（step entries）、`KnowledgeBaseDetailPage.tsx:115`（文档列表）、`WorkflowDetailPage.tsx:144`（Workflow Steps）——这些是详情页内的子表，非独立「列表页」，留作后续。
- **既有卡片基件**：多页用本地封装 `import Card from '../components/Card'`（ApiKeysPage/ConversationDetailPage/ConversationsPage/KnowledgeBasesPage/KnowledgeBaseDetailPage/ResearchPage）；`AgentRolesPage` 直接用 antd `Card`。新网格内的单卡可复用本地 `Card` 以统一视觉，或 antd `Card`。
- **搜索/筛选栏**：列表页普遍在表格上方有 `Input.Search` / `Select` 筛选（如 `ExecutionLogsPage`、`ConversationsPage`），这些独立于渲染层，改造时保留。

## §3 拟改架构（前端）

### 3.1 通用组件 `components/EntityCardGrid.tsx`
```tsx
interface EntityCardGridProps<T> {
  items: T[];
  renderCard: (item: T) => React.ReactNode;   // 页面负责单卡内容（标题/摘要/Tag/操作）
  loading?: boolean;
  emptyText?: React.ReactNode;                // 默认 '暂无数据'
  gutter?: [number, number];                  // 默认 [16,16]
  // 响应式列：默认 xs=24 sm=12 md=8 lg=6（大屏 4 列）
  onItemClick?: (item: T) => void;
}
```
- 实现：`loading` → 渲染 N 张 `Card` 包 `Skeleton active`；空 → 居中 `Empty`（用 `emptyText`）；否则 `Row gutter` + `items.map(i => <Col key={key} xs=24 sm=12 md=8 lg=6><div onClick={()=>onItemClick?.(i)}>{renderCard(i)}</div></Col>)`。
- 不内置具体操作按钮——操作由 `renderCard` 内页面自定（保证各实体语义正确），组件只负责「网格 + 加载/空态 + 响应式」。

### 3.2 各列表页改造（统一模式）
以 `WorkflowsPage` 为例：
```tsx
<EntityCardGrid
  items={list}
  loading={loading}
  emptyText="暂无工作流，点击右上角新建"
  onItemClick={(w) => navigate(`/workflows/${w.id}`)}
  renderCard={(w) => (
    <Card
      title={w.name}
      extra={<Dropdown menu={{ items: [{ key:'edit', label:'编辑' }, { key:'delete', label:'删除', danger:true }] }} trigger={['click']}><Button type="text" icon={<MoreOutlined />} /></Dropdown>}
      actions={[<span>{w.status}</span>, <span>{w.updatedAt}</span>]}
    >
      <Paragraph ellipsis={{ rows: 2 }}>{w.description}</Paragraph>
      <Tag color={...}>{w.status}</Tag>
    </Card>
  )}
/>
```
- 删除保留 `Popconfirm`（放 `Dropdown` 菜单项或卡片 `actions` 按钮）。
- 搜索/筛选栏维持原样置于网格上方。
- 分页：原 `Table pagination` 的列表若后端分页（`ExecutionLogsPage` 用 `totalCount`），卡片网格外层保留「加载更多 / 分页器」——v1 用 antd `Pagination` 置于网格下方，复用现有 `skip/take`/`totalCount` 逻辑（不引入新分页契约）。

### 3.3 状态标签 / 操作一致性
- 抽取各页既有 `status` 列渲染逻辑为卡片 `Tag`（颜色映射沿用，如 ExecutionLog 状态映射表 `B10` 已建）。
- 操作菜单：优先卡片右上 `Dropdown`（⋯）收纳 编辑/删除/运行 等，避免卡片底部按钮过多；`AgentsPage` 的 admin 操作列、`WorkflowsPage` 的「快速运行」等映射进菜单或 `actions`。

### 3.4 与 F15（i18n）的协同
- 卡片内静态文案（空态、状态词、菜单项）应走 `t()`，与 F15 一致；若本 feature 先于 F15 落地，卡片文案暂硬编码，F15 实现时统一抽取（见 §5 D3）。更优：本 feature 落地时即采用 `useTranslation()` 包裹静态文案，直接复用 F15 的 key 命名空间（需 F15 资源先建或同步建 key）。

## §4 验收子项
- **通用组件**：`EntityCardGrid` 支持 `loading` 骨架、`emptyText` 空态、响应式列（大屏 4 列、中屏 3、小屏 2、超小 1）、`onItemClick`。
- **覆盖度**：AgentsPage / AgentConfigurationsPage / WorkflowsPage / ConversationsPage / KnowledgeBasesPage / CredentialManager(凭据) / ApiKeysPage / ExecutionLogsPage / AgentRolesPage / ResearchPage 列表均改为卡片网格；视觉与原表格信息等价（标题/摘要/状态/操作不丢失）。
- **交互等价**：点击卡片进详情/抽屉；编辑/删除（`Popconfirm`）可达；搜索/筛选栏保留且仍生效；分页（如日志）保留。
- **质量门**：前端 `tsc --noEmit` 0 error（strict）、`vitest` 全过（既有 25 不回归，新增 `EntityCardGrid` 渲染/空态/响应式单测）、`vite build` 通过；`.quality-gate.json` 追加 notes 保 `cleared:true`。
- **无后端改动**：不新增/修改任何端点或契约。

## §5 决策（待锁定）
- **D1 · 执行日志（ExecutionLogsPage）是否改卡片**：默认**改**（遵循「所有列表」）——每张卡显示 状态 Tag + Agent + 耗时 + 摘要 + 时间；其多列信息压缩为卡片元信息。若团队认为日志密度更重要，可保留表格作为例外（列为决策点）。
- **D2 · 详情内子表不在 v1**：ExecutionLogDetail 的 step entries、KnowledgeBaseDetail 的文档列表、WorkflowDetail 的 Steps 属详情子表，v1 保留 `<Table>`，后续再议。
- **D3 · 与 F15 i18n 顺序**：建议 F16 落地时即采用 `t()` 包裹静态文案（与 F15 资源协同）；若 F15 未先落地，F16 建好所需 key 占位、F15 实现时合并，避免二次抽取。
- **D4 · 卡片密度**：默认大屏 4 列（`lg=6`）；列表项字段多的实体（日志）可降至 `lg=8`（3 列）以保证可读。

## §6 风险
- 🟡 中风险：涉及几乎所有列表页渲染层，工作量大；若各页自写卡片易碎片化。缓解：先落 `EntityCardGrid` 单一基件，再逐页小步替换（每页一次提交），优先高频页（Agents/Workflows/Conversations/KnowledgeBases）。
- 信息密度：表格能平铺多列，卡片需摘要 + Tag 取舍；须确保关键字段（状态/时间/owner）不丢。
- 与 F15 时序耦合：若 F16 先落地且未用 `t()`，F15 需补抽卡片文案（已在 D3 规避）。
