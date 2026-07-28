import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Table,
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

  const columns = [
    {
      title: t('pages.credentials.nameLabel'),
      dataIndex: 'name',
      key: 'name',
      render: (v: string) => <Text strong>{v}</Text>,
    },
    {
      title: t('pages.credentials.providerLabel'),
      dataIndex: 'provider',
      key: 'provider',
      render: (v: string) => <Tag color="blue">{v}</Tag>,
    },
    ...(isModel
      ? [
          {
            title: t('pages.agents.modelLabel'),
            dataIndex: 'modelName',
            key: 'modelName',
            render: (v: string | null) => v ?? <Text type="secondary">—</Text>,
          },
        ]
      : []),
    {
      title: t('pages.credentials.keyMask'),
      dataIndex: 'apiKeyMask',
      key: 'apiKeyMask',
      render: (v: string) => <Text code>{v}</Text>,
    },
    {
      title: t('common.status'),
      dataIndex: 'isEnabled',
      key: 'isEnabled',
      render: (v: boolean) =>
        v ? <Tag color="green">{t('common.enabled')}</Tag> : <Tag color="default">{t('common.disabled')}</Tag>,
    },
    {
      title: t('common.operation'),
      key: 'actions',
      render: (_: unknown, record: TenantCredentialDto) => (
        <Space>
          <Button
            size="small"
            type="link"
            icon={<EditOutlined />}
            onClick={() => openEdit(record)}
          >
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
      ),
    },
  ];

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
      <Table<TenantCredentialDto>
        rowKey="id"
        loading={loading}
        columns={columns}
        dataSource={list}
        pagination={false}
        locale={{ emptyText: t('empty.credentials') }}
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
