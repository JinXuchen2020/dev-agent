import React, { useEffect, useMemo, useState } from 'react';
import { Table, Typography, Tag, Spin, Button, Modal, Form, Select, Input, App as AntApp, Popconfirm, Space } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Agent, AgentRole, PlatformModelDto, CreateAgentRequest, UpdateAgentRequest } from '../types';
import { getAgents, getAgentRoles, getPlatformModels, createAgent, updateAgent, deleteAgent } from '../services/api';
import { useAppStore } from '../stores/appStore';
import { useTranslation } from 'react-i18next';

const { Title } = Typography;

const AgentsPage: React.FC = () => {
  const { t } = useTranslation();
  const columns: ColumnsType<Agent> = [
    { title: t('common.name'), dataIndex: 'name', key: 'name' },
    { title: t('pages.agents.roleLabel'), dataIndex: 'roleCode', key: 'roleCode' },
    { title: t('pages.agents.modelLabel'), dataIndex: 'modelName', key: 'modelName', render: (m: string | null) => m ?? <span style={{ color: '#999' }}>-</span> },
    { title: t('pages.agents.colSystemPrompt'), dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
    { title: t('common.status'), dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={s === 'Inactive' ? 'default' : 'green'}>{s ?? 'Active'}</Tag> },
    { title: t('pages.agents.colCreated'), dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
  ];
  const [agents, setAgents] = useState<Agent[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [loadingCreate, setLoadingCreate] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [models, setModels] = useState<PlatformModelDto[]>([]);
  const [editing, setEditing] = useState<Agent | null>(null);
  const { message } = AntApp.useApp();
  // RBAC: the backend seeds the admin role as "Admin" (capital A). Compare
  // case-insensitively so the UI gating matches the backend [Authorize] policy.
  const userRole = useAppStore((s) => s.userRole);
  const isAdmin = !!userRole && userRole.toLowerCase() === 'admin';
  const [form] = Form.useForm<CreateAgentRequest & { status?: string }>();

  const load = () => {
    setLoading(true);
    getAgents().then(setAgents).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);

  const openCreate = async () => {
    setEditing(null);
    setModalOpen(true);
    form.resetFields();
    form.setFieldsValue({ roleCode: 'developer', status: 'Active' });
    setLoadingCreate(true);
    try {
      const [r, m] = await Promise.all([
        getAgentRoles().catch(() => [] as AgentRole[]),
        getPlatformModels().catch(() => [] as PlatformModelDto[]),
      ]);
      setRoles(r ?? []);
      setModels(m ?? []);
    } finally {
      setLoadingCreate(false);
    }
  };

  const openEdit = async (agent: Agent) => {
    setEditing(agent);
    setModalOpen(true);
    form.resetFields();
    setLoadingCreate(true);
    try {
      const [r, m] = await Promise.all([
        getAgentRoles().catch(() => [] as AgentRole[]),
        getPlatformModels().catch(() => [] as PlatformModelDto[]),
      ]);
      setRoles(r ?? []);
      setModels(m ?? []);
      form.setFieldsValue({
        name: agent.name,
        roleCode: agent.roleCode,
        modelName: agent.modelName ?? undefined,
        systemPrompt: agent.systemPrompt,
        status: agent.status ?? 'Active',
      });
    } finally {
      setLoadingCreate(false);
    }
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    setSubmitting(true);
    try {
      // 模型下拉接 GET /api/v1/models：选中的 modelId → ModelName，provider → ModelProvider。
      const selected = models.find((m) => m.modelId === values.modelName);
      let modelProvider = selected?.provider ?? null;
      let modelName = selected?.modelId ?? null;
      // 编辑时若当前模型不在目录中（如平台已下架或 BYO 被禁用），保留原值，避免误清空。
      if (editing && !selected) {
        modelProvider = editing.modelProvider ?? null;
        modelName = editing.modelName ?? null;
      }
      const base: CreateAgentRequest = {
        name: values.name,
        roleCode: values.roleCode ?? null,
        modelProvider,
        modelName,
        modelApiUrl: null,
        systemPrompt: values.systemPrompt ?? null,
      };
      const status = values.status ?? null;

      if (editing) {
        const payload: UpdateAgentRequest = { ...base, status };
        await updateAgent(editing.id, payload);
        message.success(t('pages.agents.updated'));
      } else {
        await createAgent(base);
        message.success(t('pages.agents.created'));
      }
      setModalOpen(false);
      setEditing(null);
      load();
    } catch (e: unknown) {
      // validateFields 抛错（表单内联校验）时不重复提示；仅后端错误提示。
      if ((e as { response?: unknown })?.response) {
        message.error(t('pages.agents.saveFailed') + '：' + ((e as { message?: string }).message ?? t('pages.agents.permissionHint')));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (agent: Agent) => {
    try {
      await deleteAgent(agent.id);
      message.success(t('pages.agents.deleted'));
      load();
    } catch (e: unknown) {
      message.error(t('pages.agents.deleteFailed') + '：' + ((e as { message?: string }).message ?? t('pages.agents.permissionHint')));
    }
  };

  const modelOptions = useMemo(() => {
    // 编辑时把当前 agent 已绑定的模型也纳入选项，防止下拉里找不到。
    const all = [...models];
    if (editing?.modelName && !all.some((m) => m.modelId === editing.modelName)) {
      all.push({
        modelId: editing.modelName,
        provider: editing.modelProvider ?? '',
        displayName: `${editing.modelName}${t('pages.agents.currentModel')}`,
        isTenantOwned: false,
      });
    }
    const platform = all
      .filter((m) => !m.isTenantOwned)
      .map((m) => ({ label: m.displayName, value: m.modelId }));
    const byo = all
      .filter((m) => m.isTenantOwned)
      .map((m) => ({ label: m.displayName, value: m.modelId }));
    return [
      ...(platform.length ? [{ label: t('pages.agents.platformModels'), options: platform }] : []),
      ...(byo.length ? [{ label: t('pages.agents.myModels'), options: byo }] : []),
    ];
  }, [models, editing]);

  const actionColumn: ColumnsType<Agent>[number] = {
    title: t('common.operation'),
    key: 'actions',
    render: (_, r) => (
      <Space>
        <Button size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>
        <Popconfirm title={t('pages.agents.deleteConfirm')} okText={t('common.delete')} cancelText={t('common.cancel')} onConfirm={() => handleDelete(r)}>
          <Button size="small" danger>{t('common.delete')}</Button>
        </Popconfirm>
      </Space>
    ),
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>{t('pages.agents.title')}</Title>
        {isAdmin && (
          <Button type="primary" onClick={openCreate}>
            {t('pages.agents.newAgent')}
          </Button>
        )}
      </div>
      {loading ? (
        <Spin />
      ) : (
        <Table
          columns={isAdmin ? [...columns, actionColumn] : columns}
          dataSource={agents}
          rowKey="id"
          pagination={{ pageSize: 10 }}
        />
      )}
        <Modal
          title={editing ? t('pages.agents.editAgent') : t('pages.agents.newAgent')}
          open={modalOpen}
          onOk={handleSubmit}
          confirmLoading={submitting}
          onCancel={() => { setModalOpen(false); setEditing(null); }}
          destroyOnHidden
          okText={t('common.save')}
          cancelText={t('common.cancel')}
        >
          <Form form={form} layout="vertical">
            <Form.Item name="name" label={t('pages.agents.nameLabel')} rules={[{ required: true, message: t('pages.agents.nameRequired') }]}>
              <Input placeholder={t('pages.agents.namePlaceholder')} />
            </Form.Item>
            <Form.Item name="roleCode" label={t('pages.agents.roleLabel')}>
              <Select
                allowClear
                placeholder={t('pages.agents.rolePlaceholder')}
                loading={loadingCreate}
                options={roles.map((r) => ({ label: r.name || r.roleCode, value: r.roleCode }))}
              />
            </Form.Item>
            <Form.Item
              name="modelName"
              label={t('pages.agents.modelLabel')}
              extra={t('pages.agents.modelExtra')}
            >
              <Select
                allowClear
                showSearch
                optionFilterProp="label"
                placeholder={t('pages.agents.modelPlaceholder')}
                loading={loadingCreate}
                options={modelOptions}
              />
            </Form.Item>
            <Form.Item name="status" label={t('pages.agents.statusLabel')}>
              <Select
                options={[
                  { label: t('pages.agents.active'), value: 'Active' },
                  { label: t('pages.agents.inactive'), value: 'Inactive' },
                ]}
              />
            </Form.Item>
            <Form.Item name="systemPrompt" label={t('pages.agents.systemPromptLabel')}>
              <Input.TextArea rows={4} placeholder={t('pages.agents.systemPromptPlaceholder')} />
            </Form.Item>
          </Form>
        </Modal>
    </div>
  );
};

export default AgentsPage;
