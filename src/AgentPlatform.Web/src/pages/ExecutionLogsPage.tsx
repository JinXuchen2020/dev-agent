import React, { useEffect, useState, useCallback } from 'react';
import { Table, Typography, Tag, Space, Spin, Select } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import type { ExecutionLog } from '../types';
import { getExecutionLogs } from '../services/api';

const { Title } = Typography;

const statusColors: Record<string, string> = {
  running: 'processing', completed: 'success', failed: 'error', rolledback: 'warning', pending: 'default',
};

const ExecutionLogsPage: React.FC = () => {
  const [logs, setLogs] = useState<ExecutionLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const navigate = useNavigate();

  const fetchLogs = useCallback(() => {
    setLoading(true);
    getExecutionLogs({ status: statusFilter }).then((d) => setLogs(d.items)).finally(() => setLoading(false));
  }, [statusFilter]);

  useEffect(() => { fetchLogs(); }, [fetchLogs]);

  const columns: ColumnsType<ExecutionLog> = [
    { title: 'Workflow', dataIndex: 'workflowName', key: 'workflowName' },
    { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={statusColors[s] || 'default'}>{s}</Tag> },
    { title: 'Total Steps', dataIndex: 'totalSteps', key: 'totalSteps' },
    {
      title: 'Progress', key: 'progress',
      render: (_, r) => (
        <Space>
          <Tag color="success">{r.completedSteps} done</Tag>
          {r.failedSteps > 0 && <Tag color="error">{r.failedSteps} failed</Tag>}
        </Space>
      ),
    },
    { title: 'Started', dataIndex: 'startedAt', key: 'startedAt', render: (d: string) => new Date(d).toLocaleString() },
    { title: 'Completed', dataIndex: 'completedAt', key: 'completedAt', render: (d: string | null) => d ? new Date(d).toLocaleString() : '-' },
  ];

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Execution Logs</Title>
        <Select allowClear placeholder="Filter status" style={{ width: 160 }} value={statusFilter} onChange={setStatusFilter}
          options={[
            { value: 'running', label: 'Running' }, { value: 'completed', label: 'Completed' },
            { value: 'failed', label: 'Failed' }, { value: 'rolledback', label: 'Rolled Back' },
          ]} />
      </Space>
      {loading ? <Spin /> : (
        <Table columns={columns} dataSource={logs} rowKey="id" pagination={{ pageSize: 10 }}
          onRow={(r) => ({ onClick: () => navigate(`/execution-logs/${r.id}`), style: { cursor: 'pointer' } })} />
      )}
    </div>
  );
};

export default ExecutionLogsPage;
