import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Table, Button, Modal, Form, Input, Spin, Space, Tag, Popconfirm, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, DeleteOutlined, EyeOutlined, BookOutlined } from '@ant-design/icons';
import type { KnowledgeBase } from '../types';
import {
  getKnowledgeBases,
  createKnowledgeBase,
  deleteKnowledgeBase,
} from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';

const KnowledgeBasesPage: React.FC = () => {
  const navigate = useNavigate();
  const [list, setList] = useState<KnowledgeBase[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();

  const load = () => {
    setLoading(true);
    getKnowledgeBases()
      .then(setList)
      .catch(() => message.error('加载知识库失败'))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const handleCreate = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);
      await createKnowledgeBase({
        name: values.name,
        description: values.description ?? null,
        embeddingModel: values.embeddingModel ?? 'text-embedding-3-small',
      });
      message.success('知识库已创建');
      setCreateOpen(false);
      form.resetFields();
      load();
    } catch (e) {
      // validateFields 校验失败会抛出异常（含 errorFields），此时不应提示网络错误
      if (e && (e as { errorFields?: unknown }).errorFields) return;
      message.error('创建失败，请检查名称是否重复');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteKnowledgeBase(id);
      message.success('知识库已删除');
      load();
    } catch {
      message.error('删除失败');
    }
  };

  const columns: ColumnsType<KnowledgeBase> = [
    {
      title: '名称',
      dataIndex: 'name',
      key: 'name',
      render: (n: string) => <span style={{ color: colors.textPrimary, fontWeight: 500 }}>{n}</span>,
    },
    {
      title: '向量集合',
      dataIndex: 'collectionName',
      key: 'collectionName',
      render: (c: string) => (
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textSecondary }}>{c}</span>
      ),
    },
    {
      title: 'Embedding 模型',
      dataIndex: 'embeddingModel',
      key: 'embeddingModel',
      render: (m: string) => <Tag color="blue">{m}</Tag>,
    },
    {
      title: '文档数',
      key: 'docCount',
      width: 90,
      render: (_, r) => <span style={{ color: colors.textMuted }}>{r.documents.length}</span>,
    },
    {
      title: '创建时间',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => <span style={{ color: colors.textMuted }}>{d}</span>,
    },
    {
      title: '操作',
      key: 'actions',
      width: 170,
      render: (_, r) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} onClick={() => navigate(`/knowledge-bases/${r.id}`)}>
            查看
          </Button>
          <Popconfirm title="确认删除该知识库及其全部文档？" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="知识库"
        subtitle="RAG 知识库管理：建库、上传文档（自动切分入库）、跨租户隔离检索"
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            新建知识库
          </Button>
        }
      />

      <Card title={<span><BookOutlined style={{ marginRight: 8 }} />知识库列表</span>}>
        {loading ? (
          <Spin style={{ display: 'block', margin: '60px auto' }} />
        ) : (
          <Table columns={columns} dataSource={list} rowKey="id" pagination={false} locale={{ emptyText: '暂无知识库，点击右上角新建' }} />
        )}
      </Card>

      <Modal
        title="新建知识库"
        open={createOpen}
        onOk={handleCreate}
        confirmLoading={submitting}
        onCancel={() => setCreateOpen(false)}
        okText="创建"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" initialValues={{ embeddingModel: 'text-embedding-3-small' }}>
          <Form.Item name="name" label="知识库名称" rules={[{ required: true, message: '请输入名称' }]}>
            <Input placeholder="例如：产品文档" />
          </Form.Item>
          <Form.Item name="description" label="描述">
            <Input.TextArea rows={3} placeholder="可选" />
          </Form.Item>
          <Form.Item name="embeddingModel" label="Embedding 模型">
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default KnowledgeBasesPage;
