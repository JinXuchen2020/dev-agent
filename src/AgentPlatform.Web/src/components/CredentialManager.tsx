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

const { Paragraph, Text } = Typography;

// F13 凭据管理面板：按类别（模型 / 搜索）展示当前租户自有的全部凭据列表，
// 支持新增、编辑、删除。复用 CredentialForm 作为新增/编辑弹窗。
const CredentialManager: React.FC<{ category: CredentialCategory }> = ({ category }) => {
  const isModel = category === CredentialCategory.Model;
  const [list, setList] = useState<TenantCredentialDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<TenantCredentialDto | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    getTenantCredentials(category)
      .then(setList)
      .catch((err: unknown) => message.error('加载凭据失败：' + getErrorMessage(err)))
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
        message.success('已删除凭据');
        load();
      })
      .catch((err: unknown) => message.error('删除失败：' + getErrorMessage(err)));
  };

  const columns = [
    {
      title: '名称',
      dataIndex: 'name',
      key: 'name',
      render: (v: string) => <Text strong>{v}</Text>,
    },
    {
      title: 'Provider',
      dataIndex: 'provider',
      key: 'provider',
      render: (v: string) => <Tag color="blue">{v}</Tag>,
    },
    ...(isModel
      ? [
          {
            title: '模型',
            dataIndex: 'modelName',
            key: 'modelName',
            render: (v: string | null) => v ?? <Text type="secondary">—</Text>,
          },
        ]
      : []),
    {
      title: '密钥掩码',
      dataIndex: 'apiKeyMask',
      key: 'apiKeyMask',
      render: (v: string) => <Text code>{v}</Text>,
    },
    {
      title: '状态',
      dataIndex: 'isEnabled',
      key: 'isEnabled',
      render: (v: boolean) =>
        v ? <Tag color="green">启用</Tag> : <Tag color="default">停用</Tag>,
    },
    {
      title: '操作',
      key: 'actions',
      render: (_: unknown, record: TenantCredentialDto) => (
        <Space>
          <Button
            size="small"
            type="link"
            icon={<EditOutlined />}
            onClick={() => openEdit(record)}
          >
            编辑
          </Button>
          <Popconfirm
            title="确认删除该凭据？"
            description="删除后该模型/搜索密钥将立即失效。"
            okText="删除"
            okButtonProps={{ danger: true }}
            cancelText="取消"
            onConfirm={() => handleDelete(record)}
          >
            <Button size="small" type="link" danger icon={<DeleteOutlined />}>
              删除
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
          ? '本租户自有的模型密钥列表。你可添加多个不同模型（不同 Provider / 密钥），对话与 Agent 均可在模型下拉中选择使用。'
          : '本租户自有的搜索密钥列表（用于联网调研 Research）。'}
      </Paragraph>
      <div style={{ marginBottom: 12 }}>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          添加{isModel ? '模型' : '搜索'}凭据
        </Button>
      </div>
      <Table<TenantCredentialDto>
        rowKey="id"
        loading={loading}
        columns={columns}
        dataSource={list}
        pagination={false}
        locale={{ emptyText: '尚未添加任何凭据，点击上方按钮添加' }}
      />
      <Modal
        title={editing ? `编辑${isModel ? '模型' : '搜索'}凭据` : `添加${isModel ? '模型' : '搜索'}凭据`}
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
