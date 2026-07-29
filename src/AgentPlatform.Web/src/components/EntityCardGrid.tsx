import React from 'react';
import { Row, Col, Empty, Skeleton } from 'antd';
import Card from './Card';
import { useTranslation } from 'react-i18next';

export interface EntityCardGridProps<T> {
  items: T[];
  /** 页面负责单卡内容（标题 / 摘要 / 状态 Tag / 操作菜单） */
  renderCard: (item: T, index: number) => React.ReactNode;
  loading?: boolean;
  emptyText?: React.ReactNode;
  gutter?: [number, number];
  onItemClick?: (item: T) => void;
  rowKey?: (item: T, index: number) => React.Key;
  /** 加载骨架卡数量 */
  skeletonCount?: number;
  /**
   * 响应式列密度：
   * - normal  = 大屏 4 列（lg=6），默认；
   * - compact = 大屏 3 列（lg=8），用于字段多的实体（如执行日志）以保证可读。
   */
  density?: 'normal' | 'compact';
}

const RESPONSIVE = {
  normal: { xs: 24, sm: 12, md: 8, lg: 6 },
  compact: { xs: 24, sm: 12, md: 8, lg: 8 },
} as const;

/**
 * 通用实体卡片网格：统一各列表页的「网格 + 加载骨架 + 空态 + 响应式列」。
 * 不内置具体操作按钮——操作由 renderCard 内页面自定（保证各实体语义正确）。
 */
function EntityCardGrid<T>({
  items,
  renderCard,
  loading = false,
  emptyText,
  gutter = [16, 16],
  onItemClick,
  rowKey,
  skeletonCount = 8,
  density = 'normal',
}: EntityCardGridProps<T>): React.ReactElement {
  const { t } = useTranslation();
  const colSpan = RESPONSIVE[density];

  if (loading) {
    return (
      <Row gutter={gutter}>
        {Array.from({ length: skeletonCount }).map((_, i) => (
          <Col key={i} {...colSpan}>
            <Card>
              <Skeleton active avatar paragraph={{ rows: 2 }} />
            </Card>
          </Col>
        ))}
      </Row>
    );
  }

  if (items.length === 0) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={emptyText ?? t('empty.noData')}
        style={{ padding: '48px 0' }}
      />
    );
  }

  const handleItemClick = (item: T, e: React.MouseEvent) => {
    if (!onItemClick) return;
    // 卡片内若存在交互元素（按钮 / 链接 / 输入框等）执行自身动作，
    // 不应再触发整卡跳转，否则会出现「点删除又顺带导航」的双重动作。
    const interactive = (e.target as HTMLElement).closest(
      'button, a, input, select, textarea, [role="button"], [data-no-card-click]',
    );
    if (interactive) return;
    onItemClick(item);
  };

  return (
    <Row gutter={gutter}>
      {items.map((item, index) => (
        <Col key={rowKey ? rowKey(item, index) : index} {...colSpan}>
          <div
            onClick={onItemClick ? (e) => handleItemClick(item, e) : undefined}
            style={onItemClick ? { cursor: 'pointer' } : undefined}
          >
            {renderCard(item, index)}
          </div>
        </Col>
      ))}
    </Row>
  );
}

export default EntityCardGrid;
