import React from 'react';
import { colors, radius } from '../theme/tokens';

interface StatCardProps {
  label: string;
  value: React.ReactNode;
  /** 副文案，如 "+3 本周新增" */
  sub?: string;
  /** 副文案颜色语义 */
  subTone?: 'success' | 'warning' | 'muted';
}

const subColor: Record<NonNullable<StatCardProps['subTone']>, string> = {
  success: colors.success,
  warning: colors.warning,
  muted: colors.textMuted,
};

/** 仪表盘统计卡片 */
const StatCard: React.FC<StatCardProps> = ({ label, value, sub, subTone = 'muted' }) => (
  <div
    style={{
      flex: 1,
      background: colors.surface,
      borderRadius: radius.card,
      padding: 20,
      display: 'flex',
      flexDirection: 'column',
      gap: 8,
      border: `1px solid ${colors.border}`,
    }}
  >
    <div style={{ fontSize: 13, color: colors.textSecondary }}>{label}</div>
    <div style={{ fontSize: 32, fontWeight: 600, color: colors.textPrimary, lineHeight: 1 }}>{value}</div>
    {sub && <div style={{ fontSize: 12, color: subColor[subTone] }}>{sub}</div>}
  </div>
);

export default StatCard;
