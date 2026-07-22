import React, { useEffect, useState } from 'react';
import { Table, Spin, Button, Alert, Space, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { ApiKey } from '../types';
import { getApiKeys } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import StatusBadge from '../components/StatusBadge';
import { colors } from '../theme/tokens';

const statusLabel: Record<string, string> = {
  active: '启用',
  expiring: '即将过期',
  revoked: '已吊销',
};

const ApiKeysPage: React.FC = () => {
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getApiKeys().then(setKeys).finally(() => setLoading(false));
  }, []);

  const columns: ColumnsType<ApiKey> = [
    { title: 'Key 名称', dataIndex: 'name', key: 'name', render: (n: string) => <span style={{ color: colors.textPrimary, fontWeight: 500 }}>{n}</span> },
    {
      title: 'Key 前缀',
      dataIndex: 'prefix',
      key: 'prefix',
      render: (p: string) => <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textSecondary }}>{p}</span>,
    },
    { title: '角色', dataIndex: 'role', key: 'role', width: 120 },
    { title: '过期时间', dataIndex: 'expiresAt', key: 'expiresAt', render: (d: string) => <span style={{ color: colors.textMuted }}>{d}</span> },
    { title: '最近使用', dataIndex: 'lastUsedAt', key: 'lastUsedAt', render: (d: string | null) => <span style={{ color: colors.textMuted }}>{d ?? '-'}</span> },
    { title: '状态', dataIndex: 'status', key: 'status', width: 120, render: (s: string) => <StatusBadge status={s} label={statusLabel[s] ?? s} /> },
    {
      title: '操作',
      key: 'actions',
      width: 160,
      render: (_, r) => (
        <Space>
          <Button size="small" disabled={r.status === 'revoked'} onClick={() => message.info('轮换需后端 API 支持')}>
            轮换
          </Button>
          <Button size="small" danger disabled={r.status === 'revoked'} onClick={() => message.info('吊销需后端 API 支持')}>
            吊销
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="API Keys"
        actions={<Button type="primary">+ 新建 Key</Button>}
      />

      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 20, borderRadius: 8 }}
        message="后端当前仅提供 X-API-Key 认证方案，尚无密钥管理 REST 端点；以下为演示数据，轮换/吊销待接口就绪后接线。"
      />

      <Card title="API Key 列表">
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
