import React, { useEffect, useState, useCallback } from 'react';
import { Typography, Tag, Drawer, Descriptions, Tabs, Pagination, Space } from 'antd';
import type { AgentConfiguration } from '../types';
import { getAgentConfigurations } from '../services/api';
import CredentialManager from '../components/CredentialManager';
import { CredentialCategory } from '../types';
import { useTranslation } from 'react-i18next';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const AgentConfigurationsPage: React.FC = () => {
  const { t } = useTranslation();
  const renderConfigCard = (c: AgentConfiguration) => (
    <Card title={c.name}>
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colType')}: {c.agentType}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colVersion')}: {c.version}
        </span>
        {c.isActive ? (
          <Tag color="green">{t('common.enabled')}</Tag>
        ) : (
          <Tag>{t('common.disabled')}</Tag>
        )}
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colCreated')}: {new Date(c.createdAt).toLocaleString()}
        </span>
      </Space>
    </Card>
  );
  const [configs, setConfigs] = useState<AgentConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selected, setSelected] = useState<AgentConfiguration | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const fetch = useCallback((p: number, ps: number, signal?: AbortSignal) => {
    setLoading(true);
    getAgentConfigurations({ skip: (p - 1) * ps, take: ps, signal })
      .then((d) => {
        setConfigs(d.items);
        setTotal(d.totalCount);
      })
      .catch((err: unknown) => {
        // AbortController 取消的请求忽略；其余错误已由全局拦截器记录
        if ((err as { name?: string })?.name !== 'CanceledError')
          console.error('[AgentConfigurations] fetch failed', err);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(page, pageSize, controller.signal);
    return () => controller.abort();
  }, [fetch, page, pageSize]);

  const openDrawer = (r: AgentConfiguration) => {
    setSelected(r);
    setDrawerOpen(true);
  };

  const configsTab = (
    <>
      <EntityCardGrid
        items={configs}
        loading={loading}
        rowKey={(c) => c.id}
        emptyText={t('empty.configurations')}
        onItemClick={(c) => openDrawer(c)}
        renderCard={renderConfigCard}
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
    </>
  );

  const tabItems = [
    { key: 'configs', label: t('pages.configurations.configsTab'), children: configsTab },
    {
      key: 'creds',
      label: t('pages.configurations.credentialsTab'),
      children: (
        <Tabs
          defaultActiveKey="model"
          items={[
            {
              key: 'model',
              label: t('pages.configurations.model'),
              children: <CredentialManager category={CredentialCategory.Model} />,
            },
            {
              key: 'search',
              label: t('pages.configurations.search'),
              children: <CredentialManager category={CredentialCategory.Search} />,
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <Typography.Title level={4}>{t('pages.configurations.title')}</Typography.Title>
      <Tabs defaultActiveKey="configs" items={tabItems} />

      <Drawer
        title={t('pages.configurations.detail')}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={640}
      >
        {selected && (
          <>
            <Descriptions column={1} bordered size="small" style={{ marginBottom: 16 }}>
              <Descriptions.Item label={t('common.name')}>{selected.name}</Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colType')}>{selected.agentType}</Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colVersion')}>{selected.version}</Descriptions.Item>
              <Descriptions.Item label={t('common.enabled')}>
                {selected.isActive ? t('common.yes') : t('common.no')}
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colCreated')}>
                {new Date(selected.createdAt).toLocaleString()}
              </Descriptions.Item>
            </Descriptions>
            <pre
              style={{
                background: '#0d1117',
                color: '#e6edf3',
                padding: 16,
                borderRadius: 8,
                overflow: 'auto',
                maxHeight: 420,
                fontSize: 12,
                lineHeight: 1.5,
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                margin: 0,
              }}
            >
              {selected.yamlContent}
            </pre>
          </>
        )}
      </Drawer>
    </div>
  );
};

export default AgentConfigurationsPage;
