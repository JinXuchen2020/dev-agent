import React, { useEffect, useState } from 'react';
import { Button, Alert, Space, App as AntApp } from 'antd';
import type { ApiKey } from '../types';
import { getApiKeys } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
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

  const renderApiKeyCard = (k: ApiKey) => (
    <Card
      title={<span style={{ color: colors.textPrimary, fontWeight: 500 }}>{k.name}</span>}
      extra={
        <Space size={4}>
          <Button size="small" disabled={k.status === 'revoked'} onClick={() => message.info(t('pages.apiKeys.rotateTodo'))}>
            {t('pages.apiKeys.rotate')}
          </Button>
          <Button
            size="small"
            danger
            disabled={k.status === 'revoked'}
            onClick={() => message.info(t('pages.apiKeys.revokeTodo'))}
          >
            {t('pages.apiKeys.revoke')}
          </Button>
        </Space>
      }
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textSecondary, fontSize: 13 }}>
          {k.prefix}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.apiKeys.role')}: {k.role}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.apiKeys.expiresAt')}: {k.expiresAt}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.apiKeys.lastUsed')}: {k.lastUsedAt ?? '-'}
        </span>
        <StatusBadge
          status={k.status}
          label={
            k.status === 'active'
              ? t('pages.apiKeys.statusActive')
              : k.status === 'expiring'
                ? t('pages.apiKeys.statusExpiring')
                : k.status === 'revoked'
                  ? t('pages.apiKeys.statusRevoked')
                  : k.status
          }
        />
      </Space>
    </Card>
  );

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
        <EntityCardGrid
          items={keys}
          loading={loading}
          rowKey={(k) => k.id}
          emptyText={t('empty.noData')}
          renderCard={renderApiKeyCard}
        />
      </Card>
    </div>
  );
};

export default ApiKeysPage;
