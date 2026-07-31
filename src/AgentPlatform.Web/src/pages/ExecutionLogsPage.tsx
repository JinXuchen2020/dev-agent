import React, { useEffect, useState, useCallback } from 'react';
import { Typography, Tag, Space, Select, Pagination } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { ExecutionLog } from '../types';
import { getExecutionLogs } from '../services/api';
import { mapWorkflowStatus, WORKFLOW_STATUS_FILTER_OPTIONS } from '../status';
import { useTranslation } from 'react-i18next';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const { Title } = Typography;

const ExecutionLogsPage: React.FC = () => {
  const { t } = useTranslation();
  const [logs, setLogs] = useState<ExecutionLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<number | undefined>(undefined);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const navigate = useNavigate();

  const fetchLogs = useCallback((p: number, ps: number, status: number | undefined, signal?: AbortSignal) => {
    setLoading(true);
    getExecutionLogs({ status, skip: (p - 1) * ps, take: ps, signal })
      .then((d) => {
        setLogs(d.items);
        setTotal(d.totalCount);
      })
      .catch((err: unknown) => {
        if ((err as { name?: string })?.name !== 'CanceledError') console.error('[ExecutionLogs] fetch failed', err);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetchLogs(page, pageSize, statusFilter, controller.signal);
    return () => controller.abort();
  }, [fetchLogs, page, pageSize, statusFilter]);

  const renderLogCard = (log: ExecutionLog) => {
    const status = mapWorkflowStatus(log.status);
    return (
      <Card title={log.workflowName ?? log.id}>
        <Space direction="vertical" size={6} style={{ width: '100%' }}>
          <Tag color={status.color}>{t(status.label)}</Tag>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.executionLogs.colTotalSteps')}: {log.totalSteps}
          </span>
          <Space size={4}>
            <Tag color="success">{t('pages.executionLogs.done', { count: log.completedSteps })}</Tag>
            {log.failedSteps > 0 && (
              <Tag color="error">{t('pages.executionLogs.failed', { count: log.failedSteps })}</Tag>
            )}
          </Space>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.executionLogs.colStarted')}: {new Date(log.startedAt).toLocaleString()}
          </span>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.executionLogs.colCompleted')}: {log.completedAt ? new Date(log.completedAt).toLocaleString() : '-'}
          </span>
        </Space>
      </Card>
    );
  };

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.executionLogs.title')}
        </Title>
        <Select<number>
          allowClear
          aria-label={t('pages.executionLogs.filterStatus')}
          placeholder={t('pages.executionLogs.filterStatus')}
          style={{ width: 180 }}
          value={statusFilter}
          onChange={(v) => {
            setStatusFilter(v ?? undefined);
            setPage(1);
          }}
          options={WORKFLOW_STATUS_FILTER_OPTIONS.map((o) => ({ value: o.value, label: t(o.label) }))}
        />
      </Space>
      <EntityCardGrid
        items={logs}
        loading={loading}
        density="compact"
        rowKey={(log) => log.id}
        emptyText={t('empty.executionLogs')}
        onItemClick={(log) => navigate(`/execution-logs/${log.id}`)}
        renderCard={renderLogCard}
      />
      {!loading && total > 0 && (
        <Pagination
          style={{ marginTop: 16, textAlign: 'right' }}
          current={page}
          pageSize={pageSize}
          total={total}
          showTotal={(total) => t('common.total', { count: total })}
          onChange={(p, ps) => {
            setPage(p);
            setPageSize(ps);
          }}
        />
      )}
    </div>
  );
};

export default ExecutionLogsPage;
