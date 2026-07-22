import React from 'react';
import { colors } from '../theme/tokens';

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  actions?: React.ReactNode;
}

/** 页面顶部标题 + 右侧操作区，统一各页面头部风格 */
const PageHeader: React.FC<PageHeaderProps> = ({ title, subtitle, actions }) => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      marginBottom: 20,
      gap: 16,
    }}
  >
    <div>
      <div style={{ fontSize: 24, fontWeight: 600, color: colors.textPrimary, lineHeight: 1.2 }}>
        {title}
      </div>
      {subtitle && (
        <div style={{ fontSize: 13, color: colors.textSecondary, marginTop: 4 }}>{subtitle}</div>
      )}
    </div>
    {actions && <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>{actions}</div>}
  </div>
);

export default PageHeader;
