import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, App as AntApp, Tag, Input, Space, Select } from 'antd';
import type { Agent, Conversation, KnowledgeBase } from '../types';
import { getConversations, createConversation, getKnowledgeBases, getAgents } from '../services/api';
import {
  conversationStatusLabel,
  CONVERSATION_STATUS_META,
} from '../status';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import StatusBadge from '../components/StatusBadge';
import EntityCardGrid from '../components/EntityCardGrid';
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
  // F36：agent 筛选 + agentId→名称映射（卡片标签用）。
  const [agentFilter, setAgentFilter] = useState<string | undefined>(undefined);
  const [agentNameById, setAgentNameById] = useState<Map<string, string>>(new Map());
  const { message } = AntApp.useApp();

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    Promise.all([
      getConversations({ status: statusFilter, q: appliedQ || undefined, agentId: agentFilter, signal: controller.signal }),
      getKnowledgeBases(controller.signal).catch(() => [] as KnowledgeBase[]),
      getAgents(controller.signal).catch(() => [] as Agent[]),
    ])
      .then(([convos, kbs, agents]) => {
        setConversations(Array.isArray(convos) ? convos : []);
        setKbNameByCollection(new Map((kbs ?? []).map((kb) => [kb.collectionName, kb.name])));
        setAgentNameById(new Map((agents ?? []).map((a) => [a.id, a.name])));
      })
      .catch((err: unknown) => {
        if ((err as { name?: string })?.name !== 'CanceledError') console.error('[Conversations] fetch failed', err);
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [appliedQ, statusFilter, agentFilter]);

  const handleCreate = async () => {
    setCreating(true);
    try {
      const conv = await createConversation();
      message.success(t('pages.conversations.created'));
      if (conv?.id) navigate(`/conversations/${conv.id}`);
      else {
        const controller = new AbortController();
        // 刷新时携带当前筛选条件，避免列表与已展示的筛选状态不一致。
        getConversations({
          status: statusFilter,
          q: appliedQ || undefined,
          agentId: agentFilter,
          signal: controller.signal,
        }).then(setConversations).catch(() => undefined);
      }
    } catch {
      message.error(t('pages.conversations.createFailed'));
    } finally {
      setCreating(false);
    }
  };

  const renderConversationCard = (c: Conversation) => (
    <Card title={c.agentName ?? c.workflowId ?? c.id}>
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textPrimary, fontSize: 13 }}>
          {c.id ? (c.id.length > 16 ? `${c.id.slice(0, 16)}…` : c.id) : '-'}
        </span>
        {c.agentId && (
          <Tag color="purple">
            {t('pages.conversations.agentTag')}: {agentNameById.get(c.agentId) ?? c.agentId.slice(0, 8)}
          </Tag>
        )}
        {c.collectionName && kbNameByCollection.get(c.collectionName) ? (
          <Tag color="blue">{kbNameByCollection.get(c.collectionName)}</Tag>
        ) : (
          <span style={{ color: colors.textMuted, fontSize: 13 }}>-</span>
        )}
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.conversations.messageCount')}: {c.messages?.length ?? 0}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.conversations.status')}: <StatusBadge status={conversationStatusLabel(c.status, c.updatedAt)} />
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.conversations.startTime')}: {c.createdAt ? new Date(c.createdAt).toLocaleString() : '-'}
        </span>
      </Space>
    </Card>
  );

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
          <Select<string>
            allowClear
            showSearch
            optionFilterProp="label"
            aria-label={t('pages.conversations.agentFilter')}
            placeholder={t('pages.conversations.agentFilter')}
            style={{ width: 200 }}
            value={agentFilter}
            onChange={(v) => setAgentFilter(v ?? undefined)}
            options={[...agentNameById.entries()].map(([id, name]) => ({ value: id, label: name }))}
          />
        </Space>
        <EntityCardGrid
          items={conversations}
          loading={loading}
          rowKey={(c) => c.id}
          emptyText={t('empty.conversations')}
          onItemClick={(c) => navigate(`/conversations/${c.id}`)}
          renderCard={renderConversationCard}
        />
      </Card>
    </div>
  );
};

export default ConversationsPage;
