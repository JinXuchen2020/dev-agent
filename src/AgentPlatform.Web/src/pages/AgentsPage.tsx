import React, { useEffect, useMemo, useState } from 'react';
import { Table, Typography, Tag, Spin, Button, Modal, Form, Select, Input, App as AntApp, Popconfirm, Space } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Agent, AgentRole, PlatformModelDto, CreateAgentRequest, UpdateAgentRequest } from '../types';
import { getAgents, getAgentRoles, getPlatformModels, createAgent, updateAgent, deleteAgent } from '../services/api';
import { useAppStore } from '../stores/appStore';

const { Title } = Typography;

const columns: ColumnsType<Agent> = [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Role', dataIndex: 'roleCode', key: 'roleCode' },
  { title: 'Model', dataIndex: 'modelName', key: 'modelName', render: (m: string | null) => m ?? <span style={{ color: '#999' }}>-</span> },
  { title: 'System Prompt', dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
  { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={s === 'Inactive' ? 'default' : 'green'}>{s ?? 'Active'}</Tag> },
  { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
];

const AgentsPage: React.FC = () => {
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
        message.success('已更新 Agent');
      } else {
        await createAgent(base);
        message.success('已创建 Agent');
      }
      setModalOpen(false);
      setEditing(null);
      load();
    } catch (e: unknown) {
      // validateFields 抛错（表单内联校验）时不重复提示；仅后端错误提示。
      if ((e as { response?: unknown })?.response) {
        message.error('保存失败：' + ((e as { message?: string }).message ?? '请确认权限'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (agent: Agent) => {
    try {
      await deleteAgent(agent.id);
      message.success('已删除 Agent');
      load();
    } catch (e: unknown) {
      message.error('删除失败：' + ((e as { message?: string }).message ?? '请确认权限'));
    }
  };

  const modelOptions = useMemo(() => {
    // 编辑时把当前 agent 已绑定的模型也纳入选项，防止下拉里找不到。
    const all = [...models];
    if (editing?.modelName && !all.some((m) => m.modelId === editing.modelName)) {
      all.push({
        modelId: editing.modelName,
        provider: editing.modelProvider ?? '',
        displayName: `${editing.modelName}（当前）`,
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
      ...(platform.length ? [{ label: '平台模型', options: platform }] : []),
      ...(byo.length ? [{ label: '我的模型', options: byo }] : []),
    ];
  }, [models, editing]);

  const actionColumn: ColumnsType<Agent>[number] = {
    title: '操作',
    key: 'actions',
    render: (_, r) => (
      <Space>
        <Button size="small" onClick={() => openEdit(r)}>编辑</Button>
        <Popconfirm title="确认删除该 Agent？" okText="删除" cancelText="取消" onConfirm={() => handleDelete(r)}>
          <Button size="small" danger>删除</Button>
        </Popconfirm>
      </Space>
    ),
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Agents</Title>
        {isAdmin && (
          <Button type="primary" onClick={openCreate}>
            + 新建 Agent
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
        title={editing ? '编辑 Agent' : '新建 Agent'}
        open={modalOpen}
        onOk={handleSubmit}
        confirmLoading={submitting}
        onCancel={() => { setModalOpen(false); setEditing(null); }}
        destroyOnHidden
        okText="保存"
        cancelText="取消"
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="名称" rules={[{ required: true, message: '请输入名称' }]}>
            <Input placeholder="Agent 名称" />
          </Form.Item>
          <Form.Item name="roleCode" label="角色">
            <Select
              allowClear
              placeholder="选择角色"
              loading={loadingCreate}
              options={roles.map((r) => ({ label: r.name || r.roleCode, value: r.roleCode }))}
            />
          </Form.Item>
          <Form.Item
            name="modelName"
            label="模型"
            extra="接 GET /api/v1/models：平台内置模型与当前租户自配模型并列。"
          >
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="选择模型"
              loading={loadingCreate}
              options={modelOptions}
            />
          </Form.Item>
          <Form.Item name="status" label="状态">
            <Select
              options={[
                { label: 'Active', value: 'Active' },
                { label: 'Inactive', value: 'Inactive' },
              ]}
            />
          </Form.Item>
          <Form.Item name="systemPrompt" label="系统提示词">
            <Input.TextArea rows={4} placeholder="定义 Agent 的行为与职责" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default AgentsPage;
