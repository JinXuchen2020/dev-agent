// F40 · 回放诊断面板：从执行日志重建的异常路径（只读）。
// 设计约束：报告带 dataGaps —— 「无失败」与「信息缺失」必须在 UI 上区分，否则缺失数据会被读成健康。
import React from 'react';
import { Alert, Badge, Button, Collapse, Descriptions, Empty, Space, Tag, Timeline, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { colors } from '../theme/tokens';
import type { ReplayNodeView, ReplayReport } from '../types';

const { Text, Paragraph } = Typography;

// 后端未注册 JsonStringEnumConverter，枚举按数值传输（WorkflowState 0..5）。
const WORKFLOW_STATE_LABEL_KEY: Record<number, string> = {
  0: 'pending', 1: 'running', 2: 'paused', 3: 'completed', 4: 'failed', 5: 'rolledBack',
};

const STATE_COLOR: Record<string, string> = {
  pending: 'default', running: 'blue', paused: 'gold',
  completed: 'green', failed: 'red', rolledBack: 'orange',
};

interface ReplayPanelProps {
  report: ReplayReport | null;
  loading: boolean;
  error: string | null;
  onRetry: () => void;
}

const stateKey = (n: number): string => WORKFLOW_STATE_LABEL_KEY[n] ?? String(n);

const NodeDetail: React.FC<{ node: ReplayNodeView }> = ({ node }) => {
  const { t } = useTranslation();
  return (
    <Descriptions size="small" column={1} bordered>
      <Descriptions.Item label={t('pages.executionLogs.replay.state')}>
        <Tag color={STATE_COLOR[stateKey(node.status)] ?? 'default'}>
          {t(`pages.executionLogs.replay.states.${stateKey(node.status)}`, { defaultValue: stateKey(node.status) })}
        </Tag>
      </Descriptions.Item>
      <Descriptions.Item label={t('pages.executionLogs.replay.duration')}>
        {node.durationMs} ms
      </Descriptions.Item>
      <Descriptions.Item label={t('pages.executionLogs.replay.input')}>
        {node.input
          ? (
            <>
              <Paragraph style={{ marginBottom: 4 }} ellipsis={{ rows: 4, expandable: true, symbol: t('common.view') }}>
                {node.input}
              </Paragraph>
              {node.inputInferred && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {t('pages.executionLogs.replay.inputInferred')}
                </Text>
              )}
            </>
          )
          : <Text type="secondary">{t('pages.executionLogs.replay.notRecorded')}</Text>}
      </Descriptions.Item>
      <Descriptions.Item label={t('pages.executionLogs.replay.output')}>
        {node.output
          ? (
            <>
              <Paragraph style={{ marginBottom: 4 }} ellipsis={{ rows: 4, expandable: true, symbol: t('common.view') }}>
                {node.output}
              </Paragraph>
              {node.outputTruncated && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {t('pages.executionLogs.replay.truncated', { length: node.outputLength })}
                </Text>
              )}
            </>
          )
          : <Text type="secondary">{t('pages.executionLogs.replay.none')}</Text>}
      </Descriptions.Item>
      <Descriptions.Item label={t('pages.executionLogs.replay.error')}>
        {node.errorDetail
          ? <Text type="danger">{node.errorDetail}</Text>
          : <Text type="secondary">{t('pages.executionLogs.replay.none')}</Text>}
      </Descriptions.Item>
      <Descriptions.Item label={t('pages.executionLogs.replay.tokens')}>
        {node.tokensReported
          ? `${node.tokensIn} / ${node.tokensOut}`
          : <Text type="secondary">{t('pages.executionLogs.replay.tokensMissing')}</Text>}
      </Descriptions.Item>
    </Descriptions>
  );
};

const ReplayPanel: React.FC<ReplayPanelProps> = ({ report, loading, error, onRetry }) => {
  const { t } = useTranslation();

  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        message={t('pages.executionLogs.replay.loadFailed')}
        description={error}
        action={<Button size="small" icon={<ReloadOutlined />} onClick={onRetry}>{t('pages.errorBoundary.retry')}</Button>}
      />
    );
  }

  if (loading || !report) {
    return <Empty description={t('common.loading')} image={Empty.PRESENTED_IMAGE_SIMPLE} />;
  }

  const failed = report.failurePath;
  // 「无失败」≠「全绿」：暂停/回滚/执行中/空路径都不满足成功态，不得呈现 success 文案（避免假健康）。
  const allCompleted = report.nodes.length > 0 && report.nodes.every((n) => stateKey(n.status) === 'completed');
  const verdict: 'error' | 'success' | 'info' = failed.failedCount > 0 ? 'error' : allCompleted ? 'success' : 'info';

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {/* 结论条：失败路径 / 全绿 / 无失败但非全部成功态，三者文案必须可区分 */}
      <Alert
        type={verdict}
        showIcon
        message={failed.failedCount > 0
          ? t('pages.executionLogs.replay.foundFailures', {
            count: failed.failedCount,
            names: failed.failedStepNames.join(' → '),
            order: failed.firstFailedStepOrder,
          })
          : allCompleted
            ? t('pages.executionLogs.replay.noFailures')
            : t('pages.executionLogs.replay.noFailuresPartial')}
        description={report.missingStepCount > 0
          ? t('pages.executionLogs.replay.stepsMissing', {
            recorded: report.recordedStepCount,
            total: report.totalSteps,
            missing: report.missingStepCount,
          })
          : undefined}
      />

      {/* 数据缺口：缺失信息必须显式呈现，不能静默留白 */}
      {report.dataGaps.length > 0 && (
        <Alert
          type="warning"
          showIcon
          message={t('pages.executionLogs.replay.dataGapsTitle')}
          description={(
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {report.dataGaps.map((gap) => (
                <li key={gap}>{t(`pages.executionLogs.replay.gaps.${gap}`, { defaultValue: gap })}</li>
              ))}
            </ul>
          )}
        />
      )}

      {/* 失败路径时间线：可折叠展开每个节点的输入输出与错误 */}
      <div>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>{t('pages.executionLogs.replay.path')}</Text>
        <Timeline
          items={report.nodes.map((node, index) => ({
            color: node.isFailure ? 'red' : stateKey(node.status) === 'completed' ? 'green' : 'gray',
            children: (
              <Collapse
                size="small"
                items={[{
                  // 循环执行下多条节点可共享同一 StepOrder（handler 以 StartedAt 消歧），
                  // 故 key 必须叠加索引保证唯一，否则 Collapse activeKey 串档 + React 重复 key 告警。
                  key: `${node.stepOrder}-${index}`,
                  label: (
                    <Space>
                      <Badge count={`#${node.stepOrder}`} color={node.isFailure ? 'red' : 'default'} />
                      <Text strong={node.isFailure}>{node.stepName}</Text>
                      {node.nodeType != null && (
                        <Tag>{t(`pages.executionLogs.stepType.${node.nodeType}`, { defaultValue: `#${node.nodeType}` })}</Tag>
                      )}
                      <Text type="secondary" style={{ fontSize: 12 }}>{node.durationMs} ms</Text>
                      {node.isFailure && <Tag color="red">{t('pages.executionLogs.replay.failedTag')}</Tag>}
                    </Space>
                  ),
                  children: <NodeDetail node={node} />,
                }]}
              />
            ),
          }))}
        />
      </div>

      {/* 上下文快照：只有末次检查点，UI 必须转述后端 note，避免被当成每步快照 */}
      <div>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>{t('pages.executionLogs.replay.context')}</Text>
        {report.contextSnapshot.available ? (
          <>
            <Alert type="info" showIcon message={report.contextSnapshot.note} style={{ marginBottom: 8 }} />
            {Object.keys(report.contextSnapshot.variables).length > 0 ? (
              <Descriptions size="small" column={1} bordered>
                {Object.entries(report.contextSnapshot.variables).map(([k, v]) => (
                  <Descriptions.Item key={k} label={k}>{v}</Descriptions.Item>
                ))}
              </Descriptions>
            ) : (
              <Text type="secondary">{t('pages.executionLogs.replay.noVariables')}</Text>
            )}
          </>
        ) : (
          <Text type="secondary" style={{ color: colors.textMuted }}>
            {report.contextSnapshot.note || t('pages.executionLogs.replay.contextUnavailable')}
          </Text>
        )}
      </div>
    </Space>
  );
};

export default ReplayPanel;
