import React, { useEffect, useState } from 'react';
import { Table, Typography, Tag, Spin, Card, Space } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { AgentRole } from '../types';
import { getAgentRoles } from '../services/api';

const { Title, Text } = Typography;

const BUILT_IN_ROLES = ['architect', 'developer', 'tester', 'pm', 'tech-writer', 'reviewer'];

const columns: ColumnsType<AgentRole> = [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Role Code', dataIndex: 'roleCode', key: 'roleCode' },
  { title: 'Description', dataIndex: 'description', key: 'description', ellipsis: true },
  { title: 'System Prompt', dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
  {
    title: 'Type',
    key: 'type',
    render: (_: unknown, record: AgentRole) =>
      BUILT_IN_ROLES.includes(record.roleCode) ? <Tag color="blue">Built-in</Tag> : <Tag color="green">Custom</Tag>,
  },
];

const AgentRolesPage: React.FC = () => {
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => { getAgentRoles().then((d) => setRoles(Array.isArray(d) ? d : [])).finally(() => setLoading(false)); }, []);

  const builtIn = roles.filter((r) => BUILT_IN_ROLES.includes(r.roleCode));
  const custom = roles.filter((r) => !BUILT_IN_ROLES.includes(r.roleCode));

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <div>
      <Title level={4}>Agent Roles</Title>

      <Space direction="vertical" style={{ width: '100%' }} size="large">
        <Card
          title={<Space><Tag color="blue">Built-in</Tag><Text type="secondary">Predefined roles that ship with the platform</Text></Space>}
          size="small"
        >
          <Table
            columns={columns}
            dataSource={builtIn}
            rowKey="roleCode"
            pagination={false}
            size="small"
          />
        </Card>

        <Card
          title={<Space><Tag color="green">Custom</Tag><Text type="secondary">User-defined roles, created via API</Text></Space>}
          size="small"
        >
          <Table
            columns={columns}
            dataSource={custom}
            rowKey="roleCode"
            pagination={custom.length > 10 ? { pageSize: 10 } : false}
            size="small"
          />
        </Card>
      </Space>
    </div>
  );
};

export default AgentRolesPage;
