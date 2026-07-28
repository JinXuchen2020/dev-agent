import React, { useEffect, useState } from 'react';
import { Table, Typography, Tag, Spin, Card, Space } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { AgentRole } from '../types';
import { getAgentRoles } from '../services/api';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

const BUILT_IN_ROLES = ['architect', 'developer', 'tester', 'pm', 'tech-writer', 'reviewer'];

const AgentRolesPage: React.FC = () => {
  const { t } = useTranslation();
  const columns: ColumnsType<AgentRole> = [
    { title: t('common.name'), dataIndex: 'name', key: 'name' },
    { title: t('pages.agentRoles.colRoleCode'), dataIndex: 'roleCode', key: 'roleCode' },
    { title: t('common.description'), dataIndex: 'description', key: 'description', ellipsis: true },
    { title: t('pages.agentRoles.colSystemPrompt'), dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
    {
      title: t('pages.agentRoles.colType'),
      key: 'type',
      render: (_: unknown, record: AgentRole) =>
        BUILT_IN_ROLES.includes(record.roleCode) ? (
          <Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag>
        ) : (
          <Tag color="green">{t('pages.agentRoles.custom')}</Tag>
        ),
    },
  ];
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => { getAgentRoles().then((d) => setRoles(Array.isArray(d) ? d : [])).finally(() => setLoading(false)); }, []);

  const builtIn = roles.filter((r) => BUILT_IN_ROLES.includes(r.roleCode));
  const custom = roles.filter((r) => !BUILT_IN_ROLES.includes(r.roleCode));

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <div>
      <Title level={4}>{t('pages.agentRoles.title')}</Title>

      <Space direction="vertical" style={{ width: '100%' }} size="large">
        <Card
          title={<Space><Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag><Text type="secondary">{t('pages.agentRoles.builtInDesc')}</Text></Space>}
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
          title={<Space><Tag color="green">{t('pages.agentRoles.custom')}</Tag><Text type="secondary">{t('pages.agentRoles.customDesc')}</Text></Space>}
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
