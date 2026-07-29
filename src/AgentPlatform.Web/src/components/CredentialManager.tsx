import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Button,
  Space,
  Modal,
  Tag,
  Popconfirm,
  message,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { CredentialCategory } from '../types';
import type { TenantCredentialDto } from '../types';
import {
  getTenantCredentials,
  deleteTenantCredential,
  getErrorMessage,
} from '../services/api';
import CredentialForm from './CredentialForm';
import Card from './Card';
import EntityCardGrid from './EntityCardGrid';
import { useTranslation } from 'react-i18next';

const { Paragraph, Text } = Typography;

// F13 凭据管理面板：按类别（模型 / 搜索）展示当前租户自有的全部凭据列表，
// 支持新增、编辑、删除。复用 CredentialForm 作为新增/编辑弹窗。
const CredentialManager: React.FC<{ category: CredentialCategory }> = ({ category }) => {
  const { t } = useTranslation();
  const isModel = category === CredentialCategory.Model;
  const [list, setList] = useState<TenantCredentialDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<TenantCredentialDto | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    getTenantCredentials(category)
      .then(setList)
      .catch((err: unknown) => message.error(t('errors.loadFailed') + '：' + getErrorMessage(err)))
      .finally(() => setLoading(false));
  }, [category]);

  useEffect(() => {
    load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (record: TenantCredentialDto) => {
    setEditing(record);
    setModalOpen(true);
  };

  const handleDelete = (record: TenantCredentialDto) => {
    deleteTenantCredential(record.id)
      .then(() => {
        message.success(t('pages.credentials.deleteSuccess'));
        load();
      })
      .catch((err: unknown) => message.error(t('errors.deleteFailed') + '：' + getErrorMessage(err)));
  };

  const renderCredentialCard = (record: TenantCredentialDto) => (
    <Card
      title={<Text strong>{record.name}</Text>}
      extra={
        <Space size={4}>
          <Button size="small" type="link" icon={<EditOutlined />} onClick={() => openEdit(record)}>
            {t('common.edit')}
          </Button>
          <Popconfirm
            title={t('pages.credentials.deleteConfirm')}
            description={t('pages.credentials.deleteDesc')}
            okText={t('common.delete')}
            okButtonProps={{ danger: true }}
            cancelText={t('common.cancel')}
            onConfirm={() => handleDelete(record)}
          >
            <Button size="small" type="link" danger icon={<DeleteOutlined />}>
              {t('common.delete')}
            </Button>
          </Popconfirm>
        </Space>
      }
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <Tag color="blue">{record.provider}</Tag>
        {isModel &&
          (record.modelName ? <Text>{record.modelName}</Text> : <Text type="secondary">—</Text>)}
        <Text code>{record.apiKeyMask}</Text>
        {record.isEnabled ? (
          <Tag color="green">{t('common.enabled')}</Tag>
        ) : (
          <Tag color="default">{t('common.disabled')}</Tag>
        )}
      </Space>
    </Card>
  );

  return (
    <div>
      <Paragraph type="secondary">
        {isModel
          ? t('pages.credentials.noCredentialModel')
          : t('pages.credentials.noCredentialSearch')}
      </Paragraph>
      <div style={{ marginBottom: 12 }}>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {isModel ? t('pages.credentials.addModel') : t('pages.credentials.addSearch')}
        </Button>
      </div>
      <EntityCardGrid
        items={list}
        loading={loading}
        rowKey={(c) => c.id}
        emptyText={t('empty.credentials')}
        renderCard={renderCredentialCard}
      />
      <Modal
        title={editing ? (isModel ? t('pages.credentials.editModel') : t('pages.credentials.editSearch')) : (isModel ? t('pages.credentials.addModel') : t('pages.credentials.addSearch'))}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        footer={null}
        destroyOnHidden
      >
        <CredentialForm
          category={category}
          mode={editing ? 'edit' : 'create'}
          editing={editing}
          onSaved={() => {
            setModalOpen(false);
            load();
          }}
          onCancel={() => setModalOpen(false)}
        />
      </Modal>
    </div>
  );
};

export default CredentialManager;
