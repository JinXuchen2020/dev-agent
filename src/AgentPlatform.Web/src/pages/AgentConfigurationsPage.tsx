import React, { useEffect, useState } from 'react';
import { Table, Typography, Tag, Spin } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { AgentConfiguration } from '../types';
import { getAgentConfigurations } from '../services/api';

const { Title } = Typography;

const columns: ColumnsType<AgentConfiguration> = [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Type', dataIndex: 'agentType', key: 'agentType' },
  { title: 'Version', dataIndex: 'version', key: 'version' },
  { title: 'Active', dataIndex: 'isActive', key: 'isActive', render: (a: boolean) => a ? <Tag color="green">Active</Tag> : <Tag>Inactive</Tag> },
  { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
];

const AgentConfigurationsPage: React.FC = () => {
  const [configs, setConfigs] = useState<AgentConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => { getAgentConfigurations().then((d) => setConfigs(d.items)).finally(() => setLoading(false)); }, []);
  return (
    <div>
      <Title level={4}>Agent Configurations</Title>
      {loading ? <Spin /> : <Table columns={columns} dataSource={configs} rowKey="id" pagination={{ pageSize: 10 }} />}
    </div>
  );
};

export default AgentConfigurationsPage;
