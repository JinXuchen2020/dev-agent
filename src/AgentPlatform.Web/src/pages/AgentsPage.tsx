import React, { useEffect, useState } from 'react';
import { Table, Typography, Tag, Spin } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Agent } from '../types';
import { getAgents } from '../services/api';

const { Title } = Typography;

const columns: ColumnsType<Agent> = [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Role', dataIndex: 'role', key: 'role', render: (role: { roleCode: string }) => role?.roleCode },
  { title: 'Model', key: 'model', render: (_, r) => (r as unknown as { modelEndpoint?: { modelId: string } }).modelEndpoint?.modelId },
  { title: 'System Prompt', dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
  { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={s === 'active' ? 'green' : 'default'}>{s}</Tag> },
  { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
];

const AgentsPage: React.FC = () => {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => { getAgents().then(setAgents).finally(() => setLoading(false)); }, []);
  return (
    <div>
      <Title level={4}>Agents</Title>
      {loading ? <Spin /> : <Table columns={columns} dataSource={agents} rowKey="id" pagination={{ pageSize: 10 }} />}
    </div>
  );
};

export default AgentsPage;
