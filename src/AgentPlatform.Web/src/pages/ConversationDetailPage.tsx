import React, { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
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
} from 'antd';
import { SendOutlined } from '@ant-design/icons';
import type { Conversation, KnowledgeBase, PlatformModelDto } from '../types';
import {
  getConversation,
  getKnowledgeBases,
  setConversationKnowledgeBase,
  removeConversationKnowledgeBase,
  sendMessage,
  getPlatformModels,
  getErrorMessage,
} from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';

interface ChatMessage {
  id: string;
  role: 'user' | 'agent' | 'system';
  content: string;
}

const ConversationDetailPage: React.FC = () => {
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
        setError('加载会话失败：' + (e?.response?.data?.title ?? e.message));
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
      message.error('发送失败：' + getErrorMessage(e));
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
        message.success('已挂载知识库');
      } else {
        await removeConversationKnowledgeBase(id);
        message.success('已解除知识库');
      }
      const updated = await getConversation(id);
      setConversation(updated);
    } catch (e: unknown) {
      message.error('知识库更新失败：' + getErrorMessage(e));
    } finally {
      setSavingKb(false);
    }
  };

  if (loading) {
    return (
      <div>
        <PageHeader title="会话详情" />
        <div style={{ textAlign: 'center', padding: 80 }}>
          <Spin />
        </div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 160px)' }}>
      <PageHeader
        title="会话详情"
        actions={
          <Space>
            {linkedKbName && <Tag color="blue">已挂：{linkedKbName}</Tag>}
            <Select
              style={{ width: 220 }}
              placeholder="选择模型"
              allowClear
              value={selectedModel}
              onChange={(v) => setSelectedModel(v || undefined)}
              options={[
                ...(models.filter((m) => !m.isTenantOwned).length
                  ? [{
                      label: '平台模型',
                      options: models
                        .filter((m) => !m.isTenantOwned)
                        .map((m) => ({ label: m.displayName, value: m.modelId })),
                    }]
                  : []),
                ...(models.filter((m) => m.isTenantOwned).length
                  ? [{
                      label: '我的模型',
                      options: models
                        .filter((m) => m.isTenantOwned)
                        .map((m) => ({ label: m.displayName, value: m.modelId })),
                    }]
                  : []),
              ]}
            />
            <Select
              style={{ width: 240 }}
              placeholder="挂载知识库"
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
        title="对话"
        style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
        bodyStyle={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
      >
        {error && <Alert type="error" message={error} style={{ marginBottom: 12 }} />}
        <div style={{ flex: 1, overflowY: 'auto', padding: '8px 4px' }}>
          {messages.length === 0 ? (
            <Empty description="暂无消息，发送一条试试（若已挂知识库，将自动带入检索上下文）" />
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
            aria-label="输入消息"
            placeholder="输入消息，回车发送（Shift+Enter 换行）"
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
            发送
          </Button>
        </div>
      </Card>
    </div>
  );
};

export default ConversationDetailPage;
