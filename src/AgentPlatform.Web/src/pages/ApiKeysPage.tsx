import React, { useEffect, useState } from 'react';
import { Table, Spin, Button, Alert, Space, App as AntApp } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { ApiKey } from '../types';
import { getApiKeys } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import StatusBadge from '../components/StatusBadge';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

const ApiKeysPage: React.FC = () => {
  const { t } = useTranslation();
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loading, setLoading] = useState(true);
  const { message } = AntApp.useApp();

  useEffect(() => {
    getApiKeys().then(setKeys).finally(() => setLoading(false));
  }, []);

  const columns: ColumnsType<ApiKey> = [
    { title: t('pages.apiKeys.name'), dataIndex: 'name', key: 'name', render: (n: string) => <span style={{ color: colors.textPrimary, fontWeight: 500 }}>{n}</span> },
    {
      title: t('pages.apiKeys.prefix'),
      dataIndex: 'prefix',
      key: 'prefix',
      render: (p: string) => <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textSecondary }}>{p}</span>,
    },
    { title: t('pages.apiKeys.role'), dataIndex: 'role', key: 'role', width: 120 },
    { title: t('pages.apiKeys.expiresAt'), dataIndex: 'expiresAt', key: 'expiresAt', render: (d: string) => <span style={{ color: colors.textMuted }}>{d}</span> },
    { title: t('pages.apiKeys.lastUsed'), dataIndex: 'lastUsedAt', key: 'lastUsedAt', render: (d: string | null) => <span style={{ color: colors.textMuted }}>{d ?? '-'}</span> },
    { title: t('pages.apiKeys.status'), dataIndex: 'status', key: 'status', width: 120, render: (s: string) => <StatusBadge status={s} label={s === 'active' ? t('pages.apiKeys.statusActive') : s === 'expiring' ? t('pages.apiKeys.statusExpiring') : s === 'revoked' ? t('pages.apiKeys.statusRevoked') : s} /> },
    {
      title: t('pages.apiKeys.operation'),
      key: 'actions',
      width: 160,
      render: (_, r) => (
        <Space>
          <Button size="small" disabled={r.status === 'revoked'} onClick={() => message.info(t('pages.apiKeys.rotateTodo'))}>
            {t('pages.apiKeys.rotate')}
          </Button>
          <Button size="small" danger disabled={r.status === 'revoked'} onClick={() => message.info(t('pages.apiKeys.revokeTodo'))}>
            {t('pages.apiKeys.revoke')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('pages.apiKeys.title')}
        actions={<Button type="primary">{t('pages.apiKeys.newKey')}</Button>}
      />

      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 20, borderRadius: 8 }}
        message={t('pages.apiKeys.demoNote')}
      />

      <Card title={t('pages.apiKeys.title')}>
        {loading ? (
          <Spin style={{ display: 'block', margin: '60px auto' }} />
        ) : (
          <Table columns={columns} dataSource={keys} rowKey="id" pagination={false} />
        )}
      </Card>
    </div>
  );
};

export default ApiKeysPage;
