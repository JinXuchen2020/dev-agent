import React from 'react';
import { statusToTone, toneStyles, radius } from '../theme/tokens';

interface StatusBadgeProps {
  status: string;
  /** 显示文字，默认用 status 本身 */
  label?: string;
}

/** 浅色 pill 状态徽章，风格来自画布设计稿 */
const StatusBadge: React.FC<StatusBadgeProps> = ({ status, label }) => {
  const tone = statusToTone(status);
  const { bg, fg } = toneStyles[tone];
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        height: 24,
        padding: '0 10px',
        borderRadius: radius.badge,
        background: bg,
        color: fg,
        fontSize: 12,
        fontWeight: 500,
        lineHeight: '24px',
        whiteSpace: 'nowrap',
      }}
    >
      {label ?? status}
    </span>
  );
};

export default StatusBadge;
