import React, { useEffect, useMemo, useState } from 'react';
import { Table, Typography, Tag, Spin, Button, Modal, Form, Select, Input, App as AntApp } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { Agent, AgentRole, PlatformModelDto, CreateAgentRequest } from '../types';
import { getAgents, getAgentRoles, getPlatformModels, createAgent } from '../services/api';
import { useAppStore } from '../stores/appStore';

const { Title } = Typography;

const columns: ColumnsType<Agent> = [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Role', dataIndex: 'role', key: 'role', render: (role: { roleCode: string }) => role?.roleCode },
  { title: 'Model', key: 'model', render: (_, r) => r.modelEndpoint?.modelId },
  { title: 'System Prompt', dataIndex: 'systemPrompt', key: 'systemPrompt', ellipsis: true },
  { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Tag color={s === 'active' ? 'green' : 'default'}>{s}</Tag> },
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
  const { message } = AntApp.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const [form] = Form.useForm<CreateAgentRequest>();

  const load = () => {
    setLoading(true);
    getAgents().then(setAgents).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);

  const openCreate = async () => {
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
    } finally {
      setLoadingCreate(false);
    }
  };

  const handleCreate = async () => {
    const values = await form.validateFields();
    setSubmitting(true);
    try {
      // 模型下拉接 GET /api/v1/models：选中的 modelId → ModelName，provider → ModelProvider。
      const selected = models.find((m) => m.modelId === values.modelName);
      const payload: CreateAgentRequest = {
        name: values.name,
        roleCode: values.roleCode ?? null,
        modelProvider: selected?.provider ?? null,
        modelName: selected?.modelId ?? null,
        modelApiUrl: null,
        systemPrompt: values.systemPrompt ?? null,
      };
      await createAgent(payload);
      message.success('已创建 Agent');
      setModalOpen(false);
      load();
    } catch (e: unknown) {
      // validateFields 抛错（表单内联校验）时不重复提示；仅后端错误提示。
      if ((e as { response?: unknown })?.response) {
        message.error('创建失败：' + ((e as { message?: string }).message ?? '请确认权限'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const modelOptions = useMemo(() => {
    const platform = models
      .filter((m) => !m.isTenantOwned)
      .map((m) => ({ label: m.displayName, value: m.modelId }));
    const byo = models
      .filter((m) => m.isTenantOwned)
      .map((m) => ({ label: m.displayName, value: m.modelId }));
    return [
      ...(platform.length ? [{ label: '平台模型', options: platform }] : []),
      ...(byo.length ? [{ label: '我的模型', options: byo }] : []),
    ];
  }, [models]);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Agents</Title>
        {userRole === 'admin' && (
          <Button type="primary" loading={loadingCreate} onClick={openCreate}>
            + 新建 Agent
          </Button>
        )}
      </div>
      {loading ? (
        <Spin />
      ) : (
        <Table columns={columns} dataSource={agents} rowKey="id" pagination={{ pageSize: 10 }} />
      )}
      <Modal
        title="新建 Agent"
        open={modalOpen}
        onOk={handleCreate}
        confirmLoading={submitting}
        onCancel={() => setModalOpen(false)}
        destroyOnHidden
        okText="创建"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" initialValues={{ roleCode: 'developer' }}>
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
          <Form.Item name="systemPrompt" label="系统提示词">
            <Input.TextArea rows={4} placeholder="定义 Agent 的行为与职责" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default AgentsPage;
