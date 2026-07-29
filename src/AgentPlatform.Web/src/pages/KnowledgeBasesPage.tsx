import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Modal, Form, Input, Space, Tag, Popconfirm, App as AntApp } from 'antd';
import { PlusOutlined, DeleteOutlined, EyeOutlined, BookOutlined } from '@ant-design/icons';
import type { KnowledgeBase } from '../types';
import {
  getKnowledgeBases,
  createKnowledgeBase,
  deleteKnowledgeBase,
} from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

const KnowledgeBasesPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [list, setList] = useState<KnowledgeBase[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();
  const { message } = AntApp.useApp();

  const load = () => {
    setLoading(true);
    getKnowledgeBases()
      .then(setList)
      .catch(() => message.error(t('errors.loadFailed')))
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
      message.success(t('pages.knowledgeBases.created'));
      setCreateOpen(false);
      form.resetFields();
      load();
    } catch (e) {
      // validateFields 校验失败会抛出异常（含 errorFields），此时不应提示网络错误
      if (e && (e as { errorFields?: unknown }).errorFields) return;
      message.error(t('pages.knowledgeBases.createFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteKnowledgeBase(id);
      message.success(t('pages.knowledgeBases.deleted'));
      load();
    } catch {
      message.error(t('errors.deleteFailed'));
    }
  };

  const renderKbCard = (kb: KnowledgeBase) => (
    <Card
      title={kb.name}
      extra={
        <Space size={4}>
          <Button
            size="small"
            icon={<EyeOutlined />}
            onClick={(e) => {
              e.stopPropagation();
              navigate(`/knowledge-bases/${kb.id}`);
            }}
          >
            {t('pages.knowledgeBases.view')}
          </Button>
          <span onClick={(e) => e.stopPropagation()}>
            <Popconfirm title={t('pages.knowledgeBases.deleteConfirm')} onConfirm={() => handleDelete(kb.id)}>
              <Button size="small" danger icon={<DeleteOutlined />}>
                {t('common.delete')}
              </Button>
            </Popconfirm>
          </span>
        </Space>
      }
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", color: colors.textSecondary, fontSize: 13 }}>
          {kb.collectionName}
        </span>
        <Tag color="blue">{kb.embeddingModel}</Tag>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.knowledgeBases.docCount')}: {kb.documents.length}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.knowledgeBases.createdTime')}: {kb.createdAt}
        </span>
      </Space>
    </Card>
  );

  return (
    <div>
      <PageHeader
        title={t('pages.knowledgeBases.title')}
        subtitle={t('pages.knowledgeBases.subtitle')}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            {t('pages.knowledgeBases.newKb')}
          </Button>
        }
      />

      <Card title={<span><BookOutlined style={{ marginRight: 8 }} />{t('pages.knowledgeBases.listTitle')}</span>}>
        <EntityCardGrid
          items={list}
          loading={loading}
          rowKey={(kb) => kb.id}
          emptyText={t('pages.knowledgeBases.empty')}
          onItemClick={(kb) => navigate(`/knowledge-bases/${kb.id}`)}
          renderCard={renderKbCard}
        />
      </Card>

      <Modal
        title={t('pages.knowledgeBases.newKb')}
        open={createOpen}
        onOk={handleCreate}
        confirmLoading={submitting}
        onCancel={() => setCreateOpen(false)}
        okText={t('common.create')}
        cancelText={t('common.cancel')}
      >
        <Form form={form} layout="vertical" initialValues={{ embeddingModel: 'text-embedding-3-small' }}>
          <Form.Item name="name" label={t('pages.knowledgeBases.nameLabel')} rules={[{ required: true, message: t('pages.knowledgeBases.namePlaceholder') }]}>
            <Input placeholder={t('pages.knowledgeBases.nameExample')} />
          </Form.Item>
          <Form.Item name="description" label={t('common.description')}>
            <Input.TextArea rows={3} placeholder={t('pages.knowledgeBases.descriptionPlaceholder')} />
          </Form.Item>
          <Form.Item name="embeddingModel" label={t('pages.knowledgeBases.embeddingLabel')}>
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default KnowledgeBasesPage;
