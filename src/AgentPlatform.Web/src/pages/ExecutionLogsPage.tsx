import React, { useEffect, useState, useCallback } from 'react';
import { Table, Typography, Tag, Space, Spin, Select } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import type { ExecutionLog } from '../types';
import { getExecutionLogs } from '../services/api';
import { mapWorkflowStatus, WORKFLOW_STATUS_FILTER_OPTIONS } from '../status';

const { Title } = Typography;

const ExecutionLogsPage: React.FC = () => {
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

  const columns: ColumnsType<ExecutionLog> = [
    { title: 'Workflow', dataIndex: 'workflowName', key: 'workflowName' },
    {
      title: 'Status',
      dataIndex: 'status',
      key: 'status',
      render: (s: string | number) => {
        const m = mapWorkflowStatus(s);
        return <Tag color={m.color}>{m.label}</Tag>;
      },
    },
    { title: 'Total Steps', dataIndex: 'totalSteps', key: 'totalSteps' },
    {
      title: 'Progress',
      key: 'progress',
      render: (_, r) => (
        <Space>
          <Tag color="success">{r.completedSteps} done</Tag>
          {r.failedSteps > 0 && <Tag color="error">{r.failedSteps} failed</Tag>}
        </Space>
      ),
    },
    { title: 'Started', dataIndex: 'startedAt', key: 'startedAt', render: (d: string) => new Date(d).toLocaleString() },
    {
      title: 'Completed',
      dataIndex: 'completedAt',
      key: 'completedAt',
      render: (d: string | null) => (d ? new Date(d).toLocaleString() : '-'),
    },
  ];

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Execution Logs
        </Title>
        <Select<number>
          allowClear
          aria-label="Filter status"
          placeholder="Filter status"
          style={{ width: 180 }}
          value={statusFilter}
          onChange={(v) => {
            setStatusFilter(v ?? undefined);
            setPage(1);
          }}
          options={WORKFLOW_STATUS_FILTER_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
        />
      </Space>
      {loading ? (
        <Spin />
      ) : (
        <Table
          columns={columns}
          dataSource={logs}
          rowKey="id"
          pagination={{ current: page, pageSize, total, showTotal: (t) => `共 ${t} 条` }}
          onChange={(p) => {
            setPage(p.current ?? 1);
            setPageSize(p.pageSize ?? 10);
          }}
          onRow={(r) => ({ onClick: () => navigate(`/execution-logs/${r.id}`), style: { cursor: 'pointer' } })}
        />
      )}
    </div>
  );
};

export default ExecutionLogsPage;
