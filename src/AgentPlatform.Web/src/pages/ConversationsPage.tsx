import React, { useEffect, useState } from 'react';
import { Table, Spin, Button, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Conversation } from '../types';
import { getConversations, createConversation } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import StatusBadge from '../components/StatusBadge';
import { colors } from '../theme/tokens';

const ConversationsPage: React.FC = () => {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);

  const fetch = () => {
    setLoading(true);
    getConversations()
      .then((d) => setConversations(Array.isArray(d) ? d : []))
      .finally(() => setLoading(false));
  };
  useEffect(() => {
    fetch();
  }, []);

  const handleCreate = async () => {
    setCreating(true);
    try {
      await createConversation();
      message.success('已创建新会话');
      fetch();
    } catch {
      message.error('创建失败，请确认已登录');
    } finally {
      setCreating(false);
    }
  };

  const columns: ColumnsType<Conversation> = [
    {
      title: '会话 ID',
      dataIndex: 'id',
      key: 'id',
      render: (id: string) => (
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textPrimary }}>
          {id ? (id.length > 16 ? `${id.slice(0, 16)}…` : id) : '-'}
        </span>
      ),
    },
    {
      title: 'Agent / 工作流',
      key: 'agent',
      render: (_, r) => r.agentName ?? r.workflowId ?? '-',
    },
    {
      title: '消息数',
      key: 'msgCount',
      width: 100,
      render: (_, r) => r.messages?.length ?? 0,
    },
    {
      title: '状态',
      key: 'status',
      width: 120,
      render: (_, r) => <StatusBadge status={r.status ?? (r.updatedAt ? '已结束' : '进行中')} />,
    },
    {
      title: '开始时间',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => (
        <span style={{ color: colors.textMuted }}>
          {d ? new Date(d).toLocaleString() : '-'}
        </span>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Conversations"
        actions={
          <Button type="primary" loading={creating} onClick={handleCreate}>
            + 新建会话
          </Button>
        }
      />
      <Card title="会话列表">
        {loading ? (
          <Spin style={{ display: 'block', margin: '60px auto' }} />
        ) : (
          <Table
            columns={columns}
            dataSource={conversations}
            rowKey="id"
            pagination={{ pageSize: 10 }}
            locale={{ emptyText: '暂无会话记录' }}
          />
        )}
      </Card>
    </div>
  );
};

export default ConversationsPage;
