// 状态枚举 ↔ 前端展示 的单一事实源。
//
// 重要事实（B10 根因）：后端 API 未注册 JsonStringEnumConverter，所有枚举按
// 整数序列化（见 AgentPlatform.Api/Program.cs）。因此前端拿到的是数字而非字符串。
// 本模块统一把整数（或防御性的字符串）映射为展示标签 + antd 色 token，
// 杜绝各页面各自用小写字符串做 key 导致的色块错乱。
//
// 枚举序（与 AgentPlatform.Domain.Enums 保持一致）：
//   WorkflowState:     Pending=0 Running=1 Paused=2 Completed=3 Failed=4 RolledBack=5
//   ConversationStatus: Active=0 Closed=1 Archived=2

export type WorkflowStateValue = number | string;

export interface WorkflowStatusMeta {
  label: string;
  /** antd Tag 预设色 token */
  color: string;
}

/**
 * WorkflowState 整数枚举 → 展示元数据。
 * `label` 存放 i18n key（由页面用 t() 解析），不再硬编码文案，保证多语言化。
 * 状态文案键集中见 locales 的 `pages.workflows.status.*`。
 */
export const WORKFLOW_STATE_META: Record<number, WorkflowStatusMeta> = {
  0: { label: 'pages.workflows.status.pending', color: 'default' },
  1: { label: 'pages.workflows.status.running', color: 'processing' },
  2: { label: 'pages.workflows.status.paused', color: 'warning' },
  3: { label: 'pages.workflows.status.completed', color: 'success' },
  4: { label: 'pages.workflows.status.failed', color: 'error' },
  5: { label: 'pages.workflows.status.rolledBack', color: 'warning' },
};

/**
 * 把后端返回的状态（数字或字符串）映射为展示元数据。
 * 兼容数字与字符串两种形态（防御性），未知值回落 default。
 */
export function mapWorkflowStatus(state: WorkflowStateValue | null | undefined): WorkflowStatusMeta {
  if (state === null || state === undefined || state === '') {
    return { label: 'pages.workflows.status.unknown', color: 'default' };
  }
  const numericKey = typeof state === 'string' ? Number(state) : state;
  if (!Number.isNaN(numericKey) && WORKFLOW_STATE_META[numericKey]) {
    return WORKFLOW_STATE_META[numericKey];
  }
  const lower = String(state).toLowerCase();
  const byLabel = Object.values(WORKFLOW_STATE_META).find((m) => m.label.toLowerCase() === lower);
  return byLabel ?? { label: 'pages.workflows.status.unknown', color: 'default' };
}

/**
 * 状态筛选下拉选项。value 直接用整数枚举值——后端模型绑定对整数与
 * 大小写不敏感的名称均接受，整数最无歧义，且不再裸传小写字面量。
 */
export const WORKFLOW_STATUS_FILTER_OPTIONS: { value: number; label: string }[] = [
  { value: 0, label: 'pages.workflows.status.pending' },
  { value: 1, label: 'pages.workflows.status.running' },
  { value: 2, label: 'pages.workflows.status.paused' },
  { value: 3, label: 'pages.workflows.status.completed' },
  { value: 4, label: 'pages.workflows.status.failed' },
  { value: 5, label: 'pages.workflows.status.rolledBack' },
];

/** ConversationStatus 整数枚举 → 中文标签 + StatusBadge tone */
export const CONVERSATION_STATUS_META: Record<number, { label: string; tone: string }> = {
  0: { label: '进行中', tone: 'processing' },
  1: { label: '已结束', tone: 'success' },
  2: { label: '已归档', tone: 'default' },
};

/** 把会话状态字段（数字/字符串/未定义）归约为整数枚举值，供筛选与展示共用 */
export function conversationStatusNumber(status: string | number | undefined | null, updatedAt?: string): number {
  if (status === null || status === undefined) return updatedAt ? 1 : 0;
  const n = Number(status);
  return Number.isNaN(n) ? (updatedAt ? 1 : 0) : n;
}

/** 会话状态 → 中文标签（传给 StatusBadge，其内部按小写匹配 tone） */
export function conversationStatusLabel(status: string | number | undefined | null, updatedAt?: string): string {
  const meta = CONVERSATION_STATUS_META[conversationStatusNumber(status, updatedAt)];
  return meta?.label ?? (updatedAt ? '已结束' : '进行中');
}
