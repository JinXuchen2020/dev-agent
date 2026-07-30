import React, { useEffect, useState } from 'react';
import {
  Typography,
  Tag,
  Card,
  Space,
  Button,
  Modal,
  Form,
  Input,
  Popconfirm,
  Tooltip,
  message,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { AgentRole } from '../types';
import {
  getAgentRoles,
  createAgentRole,
  updateAgentRole,
  deleteAgentRole,
  type CreateAgentRoleRequest,
  type UpdateAgentRoleRequest,
} from '../services/api';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';

const { Title, Text, Paragraph } = Typography;

type EditingRole = AgentRole | 'new' | null;

const AgentRolesPage: React.FC = () => {
  const { t } = useTranslation();
  const userRole = useAppStore((s) => s.userRole);
  const isAdmin = !!userRole && userRole.toLowerCase() === 'admin';

  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<EditingRole>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();

  const load = () => {
    setLoading(true);
    getAgentRoles()
      .then((d) => setRoles(Array.isArray(d) ? d : []))
      .catch(() => message.error(t('common.loadFailed')))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // partition by the authoritative backend flag (no hardcoded code list)
  const builtIn = roles.filter((r) => r.isBuiltIn);
  const custom = roles.filter((r) => !r.isBuiltIn);

  const openCreate = () => {
    form.resetFields();
    setEditing('new');
  };

  const openEdit = (r: AgentRole) => {
    form.setFieldsValue({
      name: r.name,
      roleCode: r.roleCode,
      description: r.description,
      systemPrompt: r.systemPrompt,
    });
    setEditing(r);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    setSubmitting(true);
    try {
      if (editing === 'new') {
        const req: CreateAgentRoleRequest = {
          name: values.name,
          roleCode: values.roleCode,
          description: values.description,
          systemPrompt: values.systemPrompt,
        };
        await createAgentRole(req);
        message.success(t('pages.agentRoles.createSuccess'));
      } else if (editing) {
        const req: UpdateAgentRoleRequest = {
          name: values.name,
          description: values.description,
          systemPrompt: values.systemPrompt,
        };
        await updateAgentRole(editing.roleCode, req);
        message.success(t('pages.agentRoles.updateSuccess'));
      }
      setEditing(null);
      load();
    } catch (err: any) {
      const status = err?.response?.status;
      if (status === 409) {
        message.error(t('pages.agentRoles.deleteInUse'));
      } else {
        message.error(err?.response?.data?.title || err?.message || t('common.saveFailed'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (r: AgentRole) => {
    try {
      await deleteAgentRole(r.roleCode);
      message.success(t('pages.agentRoles.deleteSuccess'));
      load();
    } catch (err: any) {
      const status = err?.response?.status;
      if (status === 409) {
        message.error(r.isBuiltIn ? t('pages.agentRoles.deleteBuiltIn') : t('pages.agentRoles.deleteInUse'));
      } else {
        message.error(t('pages.agentRoles.deleteFailed'));
      }
    }
  };

  const renderActions = (r: AgentRole) => {
    if (!isAdmin) return null;
    return (
      <Space onClick={(e) => e.stopPropagation()}>
        <Tooltip title={t('pages.agentRoles.edit')}>
          <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(r)} />
        </Tooltip>
        {!r.isBuiltIn &&
          (r.agentCount > 0 ? (
            <Tooltip title={t('pages.agentRoles.deleteInUse')}>
              <Button type="text" icon={<DeleteOutlined />} disabled />
            </Tooltip>
          ) : (
            <Popconfirm
              title={t('pages.agentRoles.confirmDelete')}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
              onConfirm={() => handleDelete(r)}
            >
              <Button type="text" danger icon={<DeleteOutlined />} />
            </Popconfirm>
          ))}
      </Space>
    );
  };

  const renderRoleCard = (r: AgentRole) => (
    <Card
      title={
        <Space>
          <span>{r.name}</span>
          {r.isBuiltIn ? (
            <Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag>
          ) : (
            <Tag color="green">{t('pages.agentRoles.custom')}</Tag>
          )}
        </Space>
      }
      extra={renderActions(r)}
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <Tag color="default">{r.roleCode}</Tag>
        {r.description && <span style={{ color: colors.textMuted, fontSize: 13 }}>{r.description}</span>}
        {r.systemPrompt && (
          <Paragraph ellipsis={{ rows: 2 }} style={{ color: colors.textMuted, fontSize: 13, margin: 0 }}>
            {r.systemPrompt}
          </Paragraph>
        )}
        <Text type="secondary" style={{ fontSize: 12 }}>
          {t('pages.agentRoles.agentCount')}: {r.agentCount}
        </Text>
      </Space>
    </Card>
  );

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.agentRoles.title')}
        </Title>
        {isAdmin && (
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('pages.agentRoles.newRole')}
          </Button>
        )}
      </Space>

      <Space direction="vertical" style={{ width: '100%' }} size="large">
        <Card
          title={
            <Space>
              <Tag color="blue">{t('pages.agentRoles.builtIn')}</Tag>
              <Text type="secondary">{t('pages.agentRoles.builtInDesc')}</Text>
            </Space>
          }
          size="small"
        >
          <EntityCardGrid
            items={builtIn}
            loading={loading}
            rowKey={(r) => r.roleCode}
            renderCard={renderRoleCard}
          />
        </Card>

        <Card
          title={
            <Space>
              <Tag color="green">{t('pages.agentRoles.custom')}</Tag>
              <Text type="secondary">{t('pages.agentRoles.customDesc')}</Text>
            </Space>
          }
          size="small"
        >
          <EntityCardGrid
            items={custom}
            loading={loading}
            rowKey={(r) => r.roleCode}
            renderCard={renderRoleCard}
          />
        </Card>
      </Space>

      <Modal
        open={editing !== null}
        title={
          editing === 'new'
            ? t('pages.agentRoles.createRole')
            : `${t('pages.agentRoles.edit')}: ${editing?.name ?? ''}`
        }
        onCancel={() => setEditing(null)}
        onOk={handleSubmit}
        confirmLoading={submitting}
        destroyOnClose
        okText={t('common.save')}
        cancelText={t('common.cancel')}
      >
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="name"
            label={t('pages.agentRoles.name')}
            rules={[{ required: true, message: t('common.required') }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="roleCode"
            label={t('pages.agentRoles.code')}
            rules={[{ required: true, message: t('pages.agentRoles.requiredCode') }]}
          >
            <Input disabled={editing !== 'new'} />
          </Form.Item>
          <Form.Item name="description" label={t('pages.agentRoles.description')}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item
            name="systemPrompt"
            label={t('pages.agentRoles.systemPrompt')}
            rules={[{ required: true, message: t('common.required') }]}
          >
            <Input.TextArea rows={4} />
          </Form.Item>
        </Form>
        {editing !== 'new' && editing?.isBuiltIn && (
          <Text type="secondary" style={{ fontSize: 12 }}>
            {t('pages.agentRoles.roleCodeLocked')}
          </Text>
        )}
      </Modal>
    </div>
  );
};

export default AgentRolesPage;
