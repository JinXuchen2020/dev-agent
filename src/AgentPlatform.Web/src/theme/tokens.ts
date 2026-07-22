// 设计令牌 —— 来源于 ardot 画布「简洁浅色 SaaS」设计稿
// 所有页面统一引用，保证视觉一致

export const colors = {
  // 表面
  canvas: '#F7F8FC', // 内容区背景
  surface: '#FFFFFF', // 卡片 / 侧栏 / 顶栏底色
  surfaceMuted: '#F1F3F4', // 搜索框等浅灰底
  sidebarActive: '#1A73E8', // 侧栏选中项底色

  // 文字
  textPrimary: '#202124',
  textSecondary: '#5F6368',
  textMuted: '#9AA0A6',
  textOnAccent: '#FFFFFF',

  // 边框
  border: '#E5E7EB',

  // 品牌 & 语义色
  accent: '#1A73E8',
  success: '#34A853',
  warning: '#FBBC04',
  error: '#EA4335',

  // 状态徽章底色（浅色）
  successBg: '#E6F4EA',
  warningBg: '#FEF7E0',
  errorBg: '#FCE8E6',
  neutralBg: '#F1F3F4',
} as const;

export const radius = {
  card: 8,
  button: 6,
  pill: 24,
  badge: 12,
} as const;

export const layout = {
  sidebarWidth: 256,
  topbarHeight: 60,
  contentPadding: 28,
} as const;

export const fontStack =
  "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'PingFang SC', 'Microsoft YaHei', sans-serif";

// 语义状态 -> 徽章样式映射（中英文关键字都支持）
export type BadgeTone = 'success' | 'warning' | 'error' | 'neutral' | 'processing';

export function statusToTone(status: string): BadgeTone {
  const s = (status || '').toLowerCase();
  if (['active', 'completed', 'success', '成功', '启用', '正常', '已结束'].includes(s)) return 'success';
  if (['running', 'processing', 'pending', '进行中', '运行中', '草稿'].includes(s)) return 'processing';
  if (['warning', 'expiring', '即将过期'].includes(s)) return 'warning';
  if (['failed', 'error', 'revoked', '失败', '异常', '禁用', '已吊销', '已过期'].includes(s)) return 'error';
  return 'neutral';
}

export const toneStyles: Record<BadgeTone, { bg: string; fg: string }> = {
  success: { bg: colors.successBg, fg: colors.success },
  warning: { bg: colors.warningBg, fg: '#B06000' },
  error: { bg: colors.errorBg, fg: colors.error },
  processing: { bg: '#E8F0FE', fg: colors.accent },
  neutral: { bg: colors.neutralBg, fg: colors.textSecondary },
};
