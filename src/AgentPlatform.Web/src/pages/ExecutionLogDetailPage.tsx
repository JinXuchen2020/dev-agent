import React, { useEffect, useState, useRef, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Spin, Descriptions, Tag, Table, Card, Button, Space, Progress, Tabs } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { getExecutionLogDetail, getErrorMessage, replayExecutionLog } from '../services/api';
import type { ExecutionLogDetail, ExecutionLogStepEntry, ReplayReport } from '../types';
import ReplayPanel from '../components/ReplayPanel';
import ErrorState from '../components/ErrorState';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

const statusColors: Record<string, string> = {
  running: 'processing', completed: 'success', failed: 'error', rolledback: 'warning', pending: 'default',
};

const ExecutionLogDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [log, setLog] = useState<ExecutionLogDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // F40 回放诊断：按需加载（切到该 Tab 才请求），失败态独立于详情加载。
  const [replay, setReplay] = useState<ReplayReport | null>(null);
  const [replayLoading, setReplayLoading] = useState(false);
  const [replayError, setReplayError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('steps');
  const eventSourceRef = useRef<EventSource | null>(null);
  const { t } = useTranslation();

  const stepColumns: ColumnsType<ExecutionLogStepEntry> = [
    { title: '#', dataIndex: 'stepOrder', key: 'stepOrder', width: 50 },
    { title: t('pages.executionLogs.colStep'), dataIndex: 'stepName', key: 'stepName' },
    { title: t('common.status'), dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={statusColors[s]}>{s}</Tag> },
    { title: t('pages.executionLogs.colDuration'), dataIndex: 'duration', key: 'duration' },
    { title: t('pages.executionLogs.colResult'), dataIndex: 'result', key: 'result', ellipsis: true, render: (r: string | null) => r || '-' },
    { title: t('pages.executionLogs.colError'), dataIndex: 'errorDetail', key: 'errorDetail', ellipsis: true, render: (e: string | null) => e ? <Text type="danger">{e}</Text> : '-' },
    {
      title: t('pages.executionLogs.colNodeType'),
      dataIndex: 'nodeType',
      key: 'nodeType',
      width: 110,
      render: (n: number | null) =>
        n == null ? '-' : <Tag>{t(`pages.executionLogs.stepType.${n}`, { defaultValue: String(n) })}</Tag>,
    },
    { title: t('pages.executionLogs.colTokensIn'), dataIndex: 'tokensIn', key: 'tokensIn', width: 100, render: (v: number) => v ?? 0 },
    { title: t('pages.executionLogs.colTokensOut'), dataIndex: 'tokensOut', key: 'tokensOut', width: 100, render: (v: number) => v ?? 0 },
  ];

  const load = useCallback(() => {
    if (!id) return;
    setLoading(true);
    setError(null);
    getExecutionLogDetail(id)
      .then((d) => setLog(d))
      .catch((e: unknown) => setError(getErrorMessage(e)))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    load();
  }, [load]);

  const loadReplay = useCallback(() => {
    if (!id) return;
    setReplayLoading(true);
    setReplayError(null);
    replayExecutionLog(id)
      .then((d) => setReplay(d))
      .catch((e: unknown) => setReplayError(getErrorMessage(e)))
      .finally(() => setReplayLoading(false));
  }, [id]);

  useEffect(() => {
    // 仅在首次进入回放 Tab 时拉取；详情刷新（SSE）不重复请求。
    if (activeTab === 'replay' && !replay && !replayLoading && !replayError) loadReplay();
  }, [activeTab, replay, replayLoading, replayError, loadReplay]);

  // SSE subscription for real-time updates
  useEffect(() => {
    if (!log?.workflowId) return;
    const es = new EventSource(`/api/v1/workflows/${log.workflowId}/progress`, { withCredentials: true });
    es.onmessage = (event) => {
      try {
        // Skip server-sent keepalive comments
        if (event.data === '' || event.data.startsWith(':')) return;
        const data = JSON.parse(event.data);
        if (data.type) {
          // Refresh log detail on any progress event
          getExecutionLogDetail(id!).then(setLog).catch(() => {});
          // F40：日志在推进 → 已加载的回放报告即过期，置空让回放 Tab 的按需 effect 重取，
          // 避免把中断前的旧报告继续呈现（错误态不自动重取，由重试按钮控制）。
          setReplay((prev) => (prev ? null : prev));
        }
      } catch { /* ignore parse errors */ }
    };
    es.onerror = () => es.close();
    eventSourceRef.current = es;
    return () => es.close();
  }, [log?.workflowId, id]);

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <ErrorState message={t('pages.executionLogs.loadFailed')} description={error} onRetry={load} />;
  if (!log) return <Typography.Text type="danger">{t('pages.executionLogs.notFound')}</Typography.Text>;

  const pct = log.totalSteps > 0 ? Math.round((log.entries.length / log.totalSteps) * 100) : 0;

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/execution-logs')}>{t('common.back')}</Button>
      </Space>
      <Card>
        <Descriptions title={<Title level={4}>{log.workflowName}</Title>} column={2}>
          <Descriptions.Item label={t('common.status')}><Tag color={statusColors[log.status]}>{log.status}</Tag></Descriptions.Item>
          <Descriptions.Item label={t('pages.executionLogs.colProgress')}>
            <Progress percent={pct} size="small" style={{ width: 200 }} />
          </Descriptions.Item>
          <Descriptions.Item label={t('pages.executionLogs.colTotalSteps')}>{log.totalSteps}</Descriptions.Item>
          <Descriptions.Item label={t('pages.executionLogs.colStarted')}>{new Date(log.startedAt).toLocaleString()}</Descriptions.Item>
          <Descriptions.Item label={t('pages.executionLogs.colCompleted')}>{log.completedAt ? new Date(log.completedAt).toLocaleString() : '-'}</Descriptions.Item>
        </Descriptions>
      </Card>
      <Card style={{ marginTop: 16 }}>
        <Tabs
          activeKey={activeTab}
          onChange={setActiveTab}
          items={[
            {
              key: 'steps',
              label: t('pages.executionLogs.stepEntries'),
              children: (
                <Table columns={stepColumns} dataSource={log.entries} rowKey="id" pagination={false} size="small" />
              ),
            },
            {
              key: 'replay',
              label: (
                <span>
                  {t('pages.executionLogs.replay.tab')}
                  {replay && replay.failurePath.failedCount > 0 && (
                    <Tag color="red" style={{ marginLeft: 6 }}>{replay.failurePath.failedCount}</Tag>
                  )}
                </span>
              ),
              children: (
                <ReplayPanel
                  report={replay}
                  loading={replayLoading}
                  error={replayError}
                  onRetry={loadReplay}
                />
              ),
            },
          ]}
        />
      </Card>
    </div>
  );
};

export default ExecutionLogDetailPage;
