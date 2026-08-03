import React, { useEffect, useMemo, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Spin,
  Input,
  Button,
  Select,
  Tag,
  Alert,
  Space,
  App as AntApp,
  Empty,
  Drawer,
  List,
  Popconfirm,
  Typography,
} from 'antd';
import { SendOutlined, ArrowLeftOutlined, ThunderboltOutlined } from '@ant-design/icons';
import type { Conversation, KnowledgeBase, PlatformModelDto, WorkflowBindingDto, Workflow } from '../types';
import {
  getConversation,
  getKnowledgeBases,
  setConversationKnowledgeBase,
  removeConversationKnowledgeBase,
  sendMessage,
  getPlatformModels,
  getWorkflows,
  listConversationWorkflowBindings,
  bindWorkflow,
  unbindWorkflow,
  triggerWorkflowFromConversation,
  getErrorMessage,
} from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

interface ChatMessage {
  id: string;
  role: 'user' | 'agent' | 'system';
  content: string;
}

const ConversationDetailPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { Paragraph } = Typography;

const { id = '' } = useParams<{ id: string }>();
  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [models, setModels] = useState<PlatformModelDto[]>([]);
  const [selectedModel, setSelectedModel] = useState<string | undefined>(undefined);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(true);
  const [savingKb, setSavingKb] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { message } = AntApp.useApp();

  const load = () => {
    setLoading(true);
    setError(null);
    Promise.all([
      getConversation(id).catch((e) => {
        setError(t('pages.conversationDetail.loadFailed') + '：' + (e?.response?.data?.title ?? e.message));
        return null;
      }),
      getKnowledgeBases().catch(() => [] as KnowledgeBase[]),
      getPlatformModels().catch(() => [] as PlatformModelDto[]),
    ])
      .then(([conv, kbs, mdl]) => {
        setConversation(conv);
        setKnowledgeBases(kbs ?? []);
        setModels(mdl ?? []);
        const history: ChatMessage[] = (conv?.messages ?? [])
          .map((m, i) => ({
            id: `${i}-${m.role}`,
            role: (m.role || '').toLowerCase() as ChatMessage['role'],
            content: m.content,
          }))
          .filter((m) => m.role !== 'system');
        setMessages(history);
      })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    if (id) load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const linkedKbName = useMemo(() => {
    if (!conversation?.collectionName) return null;
    return knowledgeBases.find((kb) => kb.collectionName === conversation.collectionName)?.name ?? null;
  }, [conversation, knowledgeBases]);

  const handleSend = async () => {
    const text = input.trim();
    if (!text || sending) return;
    setInput('');
    setSending(true);
    const userMsg: ChatMessage = {
      id: `u-${Date.now()}`,
      role: 'user',
      content: text,
    };
    setMessages((prev) => [...prev, userMsg]);
    try {
      const res = await sendMessage(id, text, selectedModel ? { model: selectedModel } : undefined);
      setMessages((prev) => [
        ...prev,
        { id: `a-${Date.now()}`, role: 'agent', content: res.reply },
      ]);
    } catch (e: unknown) {
      message.error(t('pages.conversationDetail.sendFailed') + '：' + getErrorMessage(e));
      setMessages((prev) => prev.filter((m) => m.id !== userMsg.id));
    } finally {
      setSending(false);
    }
  };

  const handleKbChange = async (kbId: string | undefined) => {
    setSavingKb(true);
    try {
      if (kbId) {
        await setConversationKnowledgeBase(id, kbId);
        message.success(t('pages.conversationDetail.kbAttached'));
      } else {
        await removeConversationKnowledgeBase(id);
        message.success(t('pages.conversationDetail.kbDetached'));
      }
      const updated = await getConversation(id);
      setConversation(updated);
    } catch (e: unknown) {
      message.error(t('pages.conversationDetail.kbFailed') + '：' + getErrorMessage(e));
    } finally {
      setSavingKb(false);
    }
  };

  // ── F21 Chat 触发器：会话 ↔ 工作流绑定 ──
  const [bindDrawerOpen, setBindDrawerOpen] = useState(false);
  const [bindings, setBindings] = useState<WorkflowBindingDto[]>([]);
  const [allWorkflows, setAllWorkflows] = useState<Workflow[]>([]);
  const [bindingLoading, setBindingLoading] = useState(false);
  const [bindingActionLoading, setBindingActionLoading] = useState(false);
  const [triggeringId, setTriggeringId] = useState<string | null>(null);

  const loadBindings = (convId: string) => {
    setBindingLoading(true);
    listConversationWorkflowBindings(convId)
      .then(setBindings)
      .catch((e) => message.error(getErrorMessage(e)))
      .finally(() => setBindingLoading(false));
  };

  const openBindDrawer = () => {
    setBindDrawerOpen(true);
    loadBindings(id);
    getWorkflows({ skip: 0, take: 200 })
      .then((d) => setAllWorkflows(d.items))
      .catch(() => setAllWorkflows([]));
  };

  const handleBind = async (workflowId: string) => {
    if (!workflowId) return;
    setBindingActionLoading(true);
    try {
      await bindWorkflow(id, workflowId);
      message.success(t('pages.conversationDetail.triggers.bound'));
      loadBindings(id);
    } catch (e: unknown) {
      message.error(t('pages.conversationDetail.triggers.bindFailed') + '：' + getErrorMessage(e));
    } finally {
      setBindingActionLoading(false);
    }
  };

  const handleUnbind = async (workflowId: string) => {
    setBindingActionLoading(true);
    try {
      await unbindWorkflow(id, workflowId);
      message.success(t('pages.conversationDetail.triggers.unbound'));
      loadBindings(id);
    } catch (e: unknown) {
      message.error(t('pages.conversationDetail.triggers.unbindFailed') + '：' + getErrorMessage(e));
    } finally {
      setBindingActionLoading(false);
    }
  };

  const handleRunBound = async (workflowId: string) => {
    setTriggeringId(workflowId);
    try {
      const res = await triggerWorkflowFromConversation(id, workflowId);
      if (!res) {
        message.warning(t('pages.conversationDetail.triggers.runNotFound'));
        return;
      }
      message.success(
        t('pages.conversationDetail.triggers.runStarted', {
          name: res.workflowName,
          state: res.state,
        }),
      );
    } catch (e: unknown) {
      message.error(t('pages.conversationDetail.triggers.runFailed') + '：' + getErrorMessage(e));
    } finally {
      setTriggeringId(null);
    }
  };

  if (loading) {
      return (
        <div>
          <PageHeader
            title={
              <Space>
                <Button
                  type="text"
                  size="small"
                  icon={<ArrowLeftOutlined />}
                  onClick={() => navigate('/conversations')}
                  aria-label={t('common.back')}
                />
                {t('pages.conversationDetail.title')}
              </Space>
            }
          />
        <div style={{ textAlign: 'center', padding: 80 }}>
          <Spin />
        </div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 160px)' }}>
      <PageHeader
        title={
          <Space>
            <Button
              type="text"
              size="small"
              icon={<ArrowLeftOutlined />}
              onClick={() => navigate('/conversations')}
              aria-label={t('common.back')}
            />
            {t('pages.conversationDetail.title')}
          </Space>
        }
        actions={
          <Space>
            {linkedKbName && <Tag color="blue">{t('pages.conversationDetail.linkedKb')}：{linkedKbName}</Tag>}
            <Button
              icon={<ThunderboltOutlined />}
              onClick={openBindDrawer}
            >
              {t('pages.conversationDetail.triggers.open')}
            </Button>
            <Select
              style={{ width: 220 }}
              placeholder={t('pages.conversationDetail.modelPlaceholder')}
              allowClear
              value={selectedModel}
              onChange={(v) => setSelectedModel(v || undefined)}
              options={[
                ...(models.filter((m) => !m.isTenantOwned).length
                  ? [{
                      label: t('pages.conversationDetail.platformModels'),
                      options: models
                        .filter((m) => !m.isTenantOwned)
                        .map((m) => ({ label: m.displayName, value: m.modelId })),
                    }]
                  : []),
                ...(models.filter((m) => m.isTenantOwned).length
                  ? [{
                      label: t('pages.conversationDetail.myModels'),
                      options: models
                        .filter((m) => m.isTenantOwned)
                        .map((m) => ({ label: m.displayName, value: m.modelId })),
                    }]
                  : []),
              ]}
            />
            <Select
              style={{ width: 240 }}
              placeholder={t('pages.conversationDetail.kbPlaceholder')}
              allowClear
              loading={savingKb}
              value={conversation?.knowledgeBaseId || undefined}
              onChange={(v) => handleKbChange(v)}
              options={knowledgeBases.map((kb) => ({ label: kb.name, value: kb.id }))}
            />
          </Space>
        }
      />
      <Card
        title={t('pages.conversationDetail.dialog')}
        style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
        bodyStyle={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
      >
        {error && <Alert type="error" message={error} style={{ marginBottom: 12 }} />}
        <div style={{ flex: 1, overflowY: 'auto', padding: '8px 4px' }}>
          {messages.length === 0 ? (
            <Empty description={t('pages.conversationDetail.emptyMessages')} />
          ) : (
            messages.map((m) => (
              <div
                key={m.id}
                style={{
                  display: 'flex',
                  justifyContent: m.role === 'user' ? 'flex-end' : 'flex-start',
                  marginBottom: 12,
                }}
              >
                <div
                  style={{
                    maxWidth: '72%',
                    padding: '10px 14px',
                    borderRadius: 12,
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                    background:
                      m.role === 'user' ? colors.accent : colors.surface,
                    color: m.role === 'user' ? '#fff' : colors.textPrimary,
                  }}
                >
                  {m.content}
                </div>
              </div>
            ))
          )}
        </div>
        <div style={{ display: 'flex', gap: 8, paddingTop: 12, borderTop: `1px solid ${colors.border}` }}>
          <Input.TextArea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            aria-label={t('pages.conversationDetail.inputAria')}
            placeholder={t('pages.conversationDetail.inputPlaceholder')}
            autoSize={{ minRows: 1, maxRows: 4 }}
            onPressEnter={(e) => {
              if (!e.shiftKey) {
                e.preventDefault();
                handleSend();
              }
            }}
            disabled={sending}
          />
          <Button
            type="primary"
            icon={<SendOutlined />}
            loading={sending}
            onClick={handleSend}
            disabled={!input.trim()}
          >
            {t('pages.conversationDetail.send')}
          </Button>
        </div>
      </Card>

      <Drawer
        title={t('pages.conversationDetail.triggers.drawerTitle')}
        open={bindDrawerOpen}
        onClose={() => setBindDrawerOpen(false)}
        width={520}
      >
        <Paragraph type="secondary">{t('pages.conversationDetail.triggers.desc')}</Paragraph>
        <Space style={{ marginBottom: 16 }} wrap>
          <Select
            style={{ width: 280 }}
            placeholder={t('pages.conversationDetail.triggers.bindPlaceholder')}
            loading={bindingActionLoading}
            onChange={(v) => handleBind(v)}
            options={allWorkflows
              .filter((w) => !bindings.some((b) => b.workflowId === w.id))
              .map((w) => ({ label: w.name, value: w.id }))}
            showSearch
            optionFilterProp="label"
          />
        </Space>
        {bindingLoading ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Spin />
          </div>
        ) : bindings.length === 0 ? (
          <Empty description={t('pages.conversationDetail.triggers.listEmpty')} />
        ) : (
          <List
            dataSource={bindings}
            renderItem={(b) => (
              <List.Item
                actions={[
                  <Button
                    key="run"
                    size="small"
                    type="primary"
                    icon={<ThunderboltOutlined />}
                    loading={triggeringId === b.workflowId}
                    onClick={() => handleRunBound(b.workflowId)}
                  >
                    {t('pages.conversationDetail.triggers.run')}
                  </Button>,
                  <Popconfirm
                    key="unbind"
                    title={t('pages.conversationDetail.triggers.unbindConfirm')}
                    onConfirm={() => handleUnbind(b.workflowId)}
                    okText={t('common.confirm')}
                    cancelText={t('common.cancel')}
                  >
                    <Button size="small" danger loading={bindingActionLoading}>
                      {t('pages.conversationDetail.triggers.unbind')}
                    </Button>
                  </Popconfirm>,
                ]}
              >
                <List.Item.Meta title={b.workflowName} />
              </List.Item>
            )}
          />
        )}
      </Drawer>
    </div>
  );
};

export default ConversationDetailPage;
