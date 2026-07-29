import React, { useEffect, useState } from 'react';
import { Typography, Tag, Card, Space } from 'antd';
import type { AgentRole } from '../types';
import { getAgentRoles } from '../services/api';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

const { Title, Text, Paragraph } = Typography;

const BUILT_IN_ROLES = ['architect', 'developer', 'tester', 'pm', 'tech-writer', 'reviewer'];

const AgentRolesPage: React.FC = () => {
  const { t } = useTranslation();
  const renderRoleCard = (r: AgentRole) => (
    <Card title={r.name}>
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <Tag color="blue">{r.roleCode}</Tag>
        {r.description && <span style={{ color: colors.textMuted, fontSize: 13 }}>{r.description}</span>}
        {r.systemPrompt && (
          <Paragraph ellipsis={{ rows: 2 }} style={{ color: colors.textMuted, fontSize: 13, margin: 0 }}>
            {r.systemPrompt}
          </Paragraph>
        )}
        {BUILT_IN_ROLES.includes(r.roleCode) ? (
          <Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag>
        ) : (
          <Tag color="green">{t('pages.agentRoles.custom')}</Tag>
        )}
      </Space>
    </Card>
  );
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => { getAgentRoles().then((d) => setRoles(Array.isArray(d) ? d : [])).finally(() => setLoading(false)); }, []);

  const builtIn = roles.filter((r) => BUILT_IN_ROLES.includes(r.roleCode));
  const custom = roles.filter((r) => !BUILT_IN_ROLES.includes(r.roleCode));

  return (
    <div>
      <Title level={4}>{t('pages.agentRoles.title')}</Title>

      <Space direction="vertical" style={{ width: '100%' }} size="large">
        <Card
          title={<Space><Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag><Text type="secondary">{t('pages.agentRoles.builtInDesc')}</Text></Space>}
          size="small"
        >
          <EntityCardGrid items={builtIn} loading={loading} rowKey={(r) => r.roleCode} renderCard={renderRoleCard} />
        </Card>

        <Card
          title={<Space><Tag color="green">{t('pages.agentRoles.custom')}</Tag><Text type="secondary">{t('pages.agentRoles.customDesc')}</Text></Space>}
          size="small"
        >
          <EntityCardGrid items={custom} loading={loading} rowKey={(r) => r.roleCode} renderCard={renderRoleCard} />
        </Card>
      </Space>
    </div>
  );
};

export default AgentRolesPage;
