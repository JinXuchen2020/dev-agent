import React from 'react';
import { colors, radius } from '../theme/tokens';

interface BarChartProps {
  data: { label: string; value: number }[];
  height?: number;
}

/** 无依赖的轻量柱状图，用于仪表盘趋势展示 */
const BarChart: React.FC<BarChartProps> = ({ data, height = 200 }) => {
  const max = Math.max(...data.map((d) => d.value), 1);
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'flex-end',
        justifyContent: 'space-around',
        gap: 12,
        height,
        background: colors.canvas,
        borderRadius: radius.card,
        padding: '16px 12px 8px',
      }}
    >
      {data.map((d) => {
        const barHeight = Math.max(8, Math.round(((height - 48) * d.value) / max));
        return (
          <div
            key={d.label}
            style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, flex: 1 }}
          >
            <span style={{ fontSize: 11, color: colors.textMuted }}>{d.value}</span>
            <div
              style={{
                width: '60%',
                maxWidth: 32,
                height: barHeight,
                background: colors.accent,
                borderRadius: 3,
              }}
            />
            <span style={{ fontSize: 11, color: colors.textSecondary }}>{d.label}</span>
          </div>
        );
      })}
    </div>
  );
};

export default BarChart;
