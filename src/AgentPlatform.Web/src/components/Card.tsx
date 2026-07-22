import React from 'react';
import { colors, radius } from '../theme/tokens';

interface CardProps {
  title?: React.ReactNode;
  extra?: React.ReactNode;
  children: React.ReactNode;
  style?: React.CSSProperties;
  bodyStyle?: React.CSSProperties;
}

/** 通用白底卡片，标题行可带右侧链接/操作 */
const Card: React.FC<CardProps> = ({ title, extra, children, style, bodyStyle }) => (
  <div
    style={{
      background: colors.surface,
      borderRadius: radius.card,
      border: `1px solid ${colors.border}`,
      padding: 20,
      display: 'flex',
      flexDirection: 'column',
      gap: 16,
      ...style,
    }}
  >
    {(title || extra) && (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        {typeof title === 'string' ? (
          <span style={{ fontSize: 15, fontWeight: 600, color: colors.textPrimary }}>{title}</span>
        ) : (
          title
        )}
        {extra}
      </div>
    )}
    <div style={{ display: 'flex', flexDirection: 'column', ...bodyStyle }}>{children}</div>
  </div>
);

export default Card;
