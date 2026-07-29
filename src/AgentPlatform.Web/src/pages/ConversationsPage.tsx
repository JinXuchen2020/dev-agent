import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Table, Spin, Button, App as AntApp, Tag, Input, Space, Select } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Conversation, KnowledgeBase } from '../types';
import { getConversations, createConversation, getKnowledgeBases } from '../services/api';
import {
  conversationStatusLabel,
  CONVERSATION_STATUS_META,
} from '../status';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import StatusBadge from '../components/StatusBadge';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

const CONVERSATION_STATUS_OPTIONS = Object.entries(CONVERSATION_STATUS_META).map(([value, meta]) => ({
  value: Number(value),
  label: meta.label,
}));

const ConversationsPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [kbNameByCollection, setKbNameByCollection] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState('');
  const [appliedQ, setAppliedQ] = useState('');
  const [statusFilter, setStatusFilter] = useState<number | undefined>(undefined);
  const { message } = AntApp.useApp();

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    Promise.all([
      getConversations({ status: statusFilter, q: appliedQ || undefined, signal: controller.signal }),
      getKnowledgeBases(controller.signal).catch(() => [] as KnowledgeBase[]),
    ])
      .then(([convos, kbs]) => {
        setConversations(Array.isArray(convos) ? convos : []);
        setKbNameByCollection(new Map((kbs ?? []).map((kb) => [kb.collectionName, kb.name])));
      })
      .catch((err: unknown) => {
        if ((err as { name?: string })?.name !== 'CanceledError') console.error('[Conversations] fetch failed', err);
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [appliedQ, statusFilter]);

  const handleCreate = async () => {
    setCreating(true);
    try {
      const conv = await createConversation();
      message.success(t('pages.conversations.created'));
      if (conv?.id) navigate(`/conversations/${conv.id}`);
      else {
        const controller = new AbortController();
        getConversations({ signal: controller.signal }).then(setConversations).catch(() => undefined);
      }
    } catch {
      message.error(t('pages.conversations.createFailed'));
    } finally {
      setCreating(false);
    }
  };

  const columns: ColumnsType<Conversation> = [
    {
      title: t('pages.conversations.id'),
      dataIndex: 'id',
      key: 'id',
      render: (id: string) => (
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textPrimary }}>
          {id ? (id.length > 16 ? `${id.slice(0, 16)}…` : id) : '-'}
        </span>
      ),
    },
    {
      title: t('pages.conversations.agentWorkflow'),
      key: 'agent',
      render: (_, r) => r.agentName ?? r.workflowId ?? '-',
    },
    {
      title: t('pages.conversations.knowledgeBase'),
      key: 'kb',
      render: (_, r) =>
        r.collectionName && kbNameByCollection.get(r.collectionName) ? (
          <Tag color="blue">{kbNameByCollection.get(r.collectionName)}</Tag>
        ) : (
          <span style={{ color: colors.textMuted }}>-</span>
        ),
    },
    {
      title: t('pages.conversations.messageCount'),
      key: 'msgCount',
      width: 100,
      render: (_, r) => r.messages?.length ?? 0,
    },
    {
      title: t('pages.conversations.status'),
      key: 'status',
      width: 120,
      render: (_, r) => <StatusBadge status={conversationStatusLabel(r.status, r.updatedAt)} />,
    },
    {
      title: t('pages.conversations.startTime'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => (
        <span style={{ color: colors.textMuted }}>{d ? new Date(d).toLocaleString() : '-'}</span>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('pages.conversations.title')}
        actions={
          <Button type="primary" loading={creating} onClick={handleCreate}>
            {t('pages.conversations.newConversation')}
          </Button>
        }
      />
      <Card title={t('pages.conversations.listTitle')}>
        <Space style={{ marginBottom: 16, display: 'flex', flexWrap: 'wrap' }}>
          <Input.Search
            allowClear
            aria-label={t('pages.conversations.searchAria')}
            placeholder={t('pages.conversations.searchPlaceholder')}
            style={{ width: 320 }}
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              if (!e.target.value) setAppliedQ('');
            }}
            onSearch={(v) => setAppliedQ(v)}
          />
          <Select<number>
            allowClear
            aria-label={t('pages.conversations.statusFilter')}
            placeholder={t('pages.conversations.statusFilter')}
            style={{ width: 160 }}
            value={statusFilter}
            onChange={(v) => setStatusFilter(v ?? undefined)}
            options={CONVERSATION_STATUS_OPTIONS}
          />
        </Space>
        {loading ? (
          <Spin style={{ display: 'block', margin: '60px auto' }} />
        ) : (
          <Table
            columns={columns}
            dataSource={conversations}
            rowKey="id"
            pagination={{ pageSize: 10 }}
            locale={{ emptyText: t('pages.conversations.empty') }}
            onRow={(record) => ({
              onClick: () => navigate(`/conversations/${record.id}`),
              style: { cursor: 'pointer' },
            })}
          />
        )}
      </Card>
    </div>
  );
};

export default ConversationsPage;
