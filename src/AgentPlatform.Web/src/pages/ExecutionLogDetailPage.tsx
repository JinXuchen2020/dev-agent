import React, { useEffect, useState, useRef, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Spin, Descriptions, Tag, Table, Card, Button, Space, Progress } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { getExecutionLogDetail, getErrorMessage } from '../services/api';
import type { ExecutionLogDetail, ExecutionLogStepEntry } from '../types';
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
  const eventSourceRef = useRef<EventSource | null>(null);
  const { t } = useTranslation();

  const stepColumns: ColumnsType<ExecutionLogStepEntry> = [
    { title: '#', dataIndex: 'stepOrder', key: 'stepOrder', width: 50 },
    { title: t('pages.executionLogs.colStep'), dataIndex: 'stepName', key: 'stepName' },
    { title: t('common.status'), dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={statusColors[s]}>{s}</Tag> },
    { title: t('pages.executionLogs.colDuration'), dataIndex: 'duration', key: 'duration' },
    { title: t('pages.executionLogs.colResult'), dataIndex: 'result', key: 'result', ellipsis: true, render: (r: string | null) => r || '-' },
    { title: t('pages.executionLogs.colError'), dataIndex: 'errorDetail', key: 'errorDetail', ellipsis: true, render: (e: string | null) => e ? <Text type="danger">{e}</Text> : '-' },
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
      <Card title={t('pages.executionLogs.stepEntries')} style={{ marginTop: 16 }}>
        <Table columns={stepColumns} dataSource={log.entries} rowKey="id" pagination={false} size="small" />
      </Card>
    </div>
  );
};

export default ExecutionLogDetailPage;
