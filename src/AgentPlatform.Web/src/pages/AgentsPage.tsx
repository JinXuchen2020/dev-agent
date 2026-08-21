import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Typography, Tag, Button, Modal, Form, Select, Input, InputNumber, App as AntApp, Popconfirm, Space, theme, Empty, Skeleton, Alert } from 'antd';
import type { Agent, AgentRole, PlatformModelDto, CreateAgentRequest, UpdateAgentRequest, AgentConfiguration, ConfigurationAgentTemplate, AgenticRunResponse } from '../types';
import { AgentConfigurationStatus } from '../types';
import { getAgents, getAgentRoles, getPlatformModels, createAgent, updateAgent, deleteAgent, getAgentConfigurations, getAgentConfigurationTemplate, runAgentGoal } from '../services/api';
import { useAppStore } from '../stores/appStore';
import { useTranslation } from 'react-i18next';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const { Title, Paragraph } = Typography;

// F29: workspace tools that can be granted to an autonomous agent.
const WORKSPACE_TOOL_OPTIONS = ['read_file', 'write_file', 'edit_file', 'list_files', 'run_command', 'git_diff'];

const AgentsPage: React.FC = () => {
  const { t } = useTranslation();
  const renderAgentCard = (agent: Agent) => (
    <Card
      title={agent.name}
      extra={
        <Space size={4}>
          {canRun && (
            <Button size="small" onClick={() => openRun(agent)}>
              {t('pages.agents.runAgent')}
            </Button>
          )}
          {isAdmin && (
            <>
              <Button size="small" onClick={() => openEdit(agent)}>
                {t('common.edit')}
              </Button>
              <Popconfirm
                title={t('pages.agents.deleteConfirm')}
                okText={t('common.delete')}
                cancelText={t('common.cancel')}
                onConfirm={() => handleDelete(agent)}
              >
                <Button size="small" danger>
                  {t('common.delete')}
                </Button>
              </Popconfirm>
            </>
          )}
        </Space>
      }
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.agents.roleLabel')}: {agent.roleCode ?? '-'}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.agents.modelLabel')}: {agent.modelName ?? '-'}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.agents.colCreated')}: {new Date(agent.createdAt).toLocaleString()}
        </span>
        {agent.systemPrompt && (
          <Paragraph ellipsis={{ rows: 2 }} style={{ color: colors.textMuted, fontSize: 13, margin: 0 }}>
            {agent.systemPrompt}
          </Paragraph>
        )}
        {agent.allowedToolNames && agent.allowedToolNames.length > 0 && (
          <Space size={4} wrap>
            <Tag color="blue">{t('pages.agents.allowedTools')}: {agent.allowedToolNames.length}</Tag>
            {agent.maxIterations != null && <Tag>{t('pages.agents.maxIterations')}: {agent.maxIterations}</Tag>}
          </Space>
        )}
        <Tag color={agent.status === 'Inactive' ? 'default' : 'green'}>{agent.status ?? 'Active'}</Tag>
      </Space>
    </Card>
  );
  const [agents, setAgents] = useState<Agent[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [loadingCreate, setLoadingCreate] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [models, setModels] = useState<PlatformModelDto[]>([]);
  const [editing, setEditing] = useState<Agent | null>(null);
  // F29 agentic run 弹窗状态
  const [runAgent, setRunAgent] = useState<Agent | null>(null);
  const [runGoal, setRunGoal] = useState('');
  const [runLoading, setRunLoading] = useState(false);
  const [runResult, setRunResult] = useState<AgenticRunResponse | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const { message } = AntApp.useApp();
  // RBAC: the backend seeds the admin role as "Admin" (capital A). Compare
  // case-insensitively so the UI gating matches the backend [Authorize] policy.
  const userRole = useAppStore((s) => s.userRole);
  const isAdmin = !!userRole && userRole.toLowerCase() === 'admin';
  // POST /agents/{id}/runs 允许 Admin,Operator。
  const canRun = isAdmin || (!!userRole && userRole.toLowerCase() === 'operator');
  const [form] = Form.useForm<CreateAgentRequest & { status?: string }>();

  // "基于模板新建" 状态：模板选择弹窗 + 配置列表 + 溯源 id。
  const [templateModalOpen, setTemplateModalOpen] = useState(false);
  const [templateConfigs, setTemplateConfigs] = useState<AgentConfiguration[]>([]);
  const [templateLoading, setTemplateLoading] = useState(false);
  const pendingConfigurationId = useRef<string | null>(null);

  const { token } = theme.useToken();

  const configStatusLabel = (s: AgentConfigurationStatus): { label: string; color: string } => {
    switch (s) {
      case AgentConfigurationStatus.Active:
        return { label: t('pages.configurations.statusActive'), color: 'green' };
      case AgentConfigurationStatus.Archived:
        return { label: t('pages.configurations.statusArchived'), color: 'default' };
      case AgentConfigurationStatus.Deprecated:
        return { label: t('pages.configurations.statusDeprecated'), color: 'red' };
      default:
        return { label: t('pages.configurations.statusDraft'), color: 'gold' };
    }
  };

  const load = () => {
    setLoading(true);
    getAgents().then(setAgents).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, []);

  const openCreate = async () => {
    setEditing(null);
    pendingConfigurationId.current = null;
    setModalOpen(true);
    form.resetFields();
    form.setFieldsValue({ roleCode: 'development', status: 'Active' });
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
        allowedToolNames: agent.allowedToolNames ?? [],
        maxIterations: agent.maxIterations ?? 25,
        stopCriteria: agent.stopCriteria ?? undefined,
      });
    } finally {
      setLoadingCreate(false);
    }
  };

  const openTemplatePicker = async () => {
    setTemplateModalOpen(true);
    setTemplateLoading(true);
    try {
      const d = await getAgentConfigurations({ take: 100 }).catch(() => ({
        items: [] as AgentConfiguration[],
        totalCount: 0,
      }));
      const sorted = [...(d?.items ?? [])].sort((a, b) => {
        const av = a.status === AgentConfigurationStatus.Active ? 0 : 1;
        const bv = b.status === AgentConfigurationStatus.Active ? 0 : 1;
        if (av !== bv) return av - bv;
        return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
      });
      setTemplateConfigs(sorted);
    } finally {
      setTemplateLoading(false);
    }
  };

  // 选模板 → 拉取结构化 template → 复用新建表单并预填（保留 status=Active 默认）。
  const chooseTemplate = async (cfg: AgentConfiguration) => {
    setTemplateModalOpen(false);
    setEditing(null);
    pendingConfigurationId.current = null;
    setModalOpen(true);
    form.resetFields();
    setLoadingCreate(true);
    try {
      const [r, m, tpl] = await Promise.all([
        getAgentRoles().catch(() => [] as AgentRole[]),
        getPlatformModels().catch(() => [] as PlatformModelDto[]),
        getAgentConfigurationTemplate(cfg.id).catch(() => null as ConfigurationAgentTemplate | null),
      ]);
      setRoles(r ?? []);
      setModels(m ?? []);
      const defaults: Partial<CreateAgentRequest & { status?: string }> = {
        roleCode: 'development',
        status: 'Active',
      };
      if (tpl) {
        pendingConfigurationId.current = tpl.configurationId;
        defaults.name = tpl.name;
        defaults.roleCode = tpl.roleCode ?? 'development';
        defaults.systemPrompt = tpl.systemPrompt ?? '';
        // 模型下拉接目录 modelId；若模板模型命中目录则 provider 自动解析，否则注入一条合成
        // 目录项，避免 handleSubmit 的 models.find 解析不到 provider 而静默丢弃模型。
        if (tpl.modelName) {
          defaults.modelName = tpl.modelName;
          if (!m.some((mm) => mm.modelId === tpl.modelName)) {
            setModels([
              ...m,
              {
                modelId: tpl.modelName,
                provider: tpl.modelProvider ?? '',
                displayName: tpl.modelName,
                isTenantOwned: false,
              },
            ]);
          }
        }
      }
      form.setFieldsValue(defaults as Parameters<typeof form.setFieldsValue>[0]);
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
        allowedToolNames: values.allowedToolNames && values.allowedToolNames.length > 0 ? values.allowedToolNames : null,
        maxIterations: values.maxIterations ?? null,
        stopCriteria: values.stopCriteria || null,
      };
      const status = values.status ?? null;

      if (editing) {
        const payload: UpdateAgentRequest = { ...base, status };
        await updateAgent(editing.id, payload);
        message.success(t('pages.agents.updated'));
      } else {
        await createAgent({ ...base, configurationId: pendingConfigurationId.current });
        pendingConfigurationId.current = null;
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

  const openRun = (agent: Agent) => {
    setRunAgent(agent);
    setRunGoal('');
    setRunResult(null);
    setRunError(null);
  };

  const handleRun = async () => {
    if (!runAgent || !runGoal.trim()) return;
    setRunLoading(true);
    setRunResult(null);
    setRunError(null);
    try {
      const result = await runAgentGoal(runAgent.id, runGoal.trim());
      setRunResult(result);
      message.success(t('pages.agents.runSuccess'));
    } catch (e: unknown) {
      setRunError((e as { message?: string }).message ?? t('pages.agents.runFailed'));
    } finally {
      setRunLoading(false);
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

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>{t('pages.agents.title')}</Title>
        {isAdmin && (
          <Space>
            <Button type="primary" onClick={openCreate}>
              {t('pages.agents.newAgent')}
            </Button>
            <Button onClick={openTemplatePicker}>
              {t('pages.agents.fromTemplate')}
            </Button>
          </Space>
        )}
      </div>
      <EntityCardGrid
        items={agents}
        loading={loading}
        rowKey={(a) => a.id}
        emptyText={t('empty.agents')}
        renderCard={renderAgentCard}
      />
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
            <Form.Item name="allowedToolNames" label={t('pages.agents.allowedTools')}>
              <Select
                mode="multiple"
                allowClear
                placeholder={t('pages.agents.allowedToolsPlaceholder')}
                options={WORKSPACE_TOOL_OPTIONS.map((name) => ({ label: name, value: name }))}
              />
            </Form.Item>
            <Form.Item name="maxIterations" label={t('pages.agents.maxIterations')} extra={t('pages.agents.maxIterationsExtra')}>
              <InputNumber min={1} max={200} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="stopCriteria" label={t('pages.agents.stopCriteria')}>
              <Input placeholder={t('pages.agents.stopCriteriaPlaceholder')} />
            </Form.Item>
          </Form>
        </Modal>

        <Modal
          title={t('pages.agents.templatePicker')}
          open={templateModalOpen}
          footer={null}
          onCancel={() => setTemplateModalOpen(false)}
          width={640}
        >
          <Typography.Paragraph type="secondary">{t('pages.agents.templateHint')}</Typography.Paragraph>
          {templateLoading ? (
            <Skeleton active />
          ) : templateConfigs.length === 0 ? (
            <Empty description={t('empty.configurations')} />
          ) : (
            <div style={{ maxHeight: 420, overflow: 'auto' }}>
              {templateConfigs.map((c) => (
                <div
                  key={c.id}
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    padding: '12px 4px',
                    borderBottom: `1px solid ${token.colorBorderSecondary}`,
                  }}
                >
                  <div>
                    <div style={{ fontWeight: 600 }}>
                      {c.name}{' '}
                      <Tag color={configStatusLabel(c.status).color}>
                        {configStatusLabel(c.status).label}
                      </Tag>
                    </div>
                    <div style={{ color: colors.textMuted, fontSize: 12 }}>
                      {t('pages.configurations.colVersion')}: {c.version} ·{' '}
                      {c.agentTypeCode ?? '-'}
                    </div>
                  </div>
                  <Button type="primary" onClick={() => chooseTemplate(c)}>
                    {t('pages.agents.useTemplate')}
                  </Button>
                </div>
              ))}
            </div>
          )}
        </Modal>

        <Modal
          title={`${t('pages.agents.runAgent')}${runAgent ? ` — ${runAgent.name}` : ''}`}
          open={!!runAgent}
          onCancel={() => setRunAgent(null)}
          footer={null}
          width={720}
          destroyOnHidden
        >
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Input.TextArea
              rows={3}
              value={runGoal}
              onChange={(e) => setRunGoal(e.target.value)}
              placeholder={t('pages.agents.runGoalPlaceholder')}
            />
            <Button type="primary" loading={runLoading} disabled={!runGoal.trim()} onClick={handleRun} style={{ alignSelf: 'flex-start' }}>
              {t('pages.agents.runExecute')}
            </Button>
            {runError && <Alert type="error" showIcon message={runError} />}
            {runResult && (
              <div>
                <Alert
                  type="success"
                  showIcon
                  message={t('pages.agents.runResult')}
                  description={`${t('pages.agents.runIterations')}: ${runResult.iterations} · Tokens: ${runResult.totalTokensIn}/${runResult.totalTokensOut}`}
                />
                <Paragraph style={{ marginTop: 12, fontWeight: 600 }}>{t('pages.agents.runFinalAnswer')}</Paragraph>
                <Paragraph style={{ whiteSpace: 'pre-wrap', background: colors.surfaceMuted, padding: 12, borderRadius: 8 }}>
                  {runResult.finalAnswer}
                </Paragraph>
                <Paragraph style={{ marginTop: 12, fontWeight: 600 }}>{t('pages.agents.runTrace')}</Paragraph>
                <Space direction="vertical" size={4} style={{ width: '100%' }}>
                  {runResult.trace.map((step, i) => (
                    <div key={i} style={{ fontSize: 13 }}>
                      <Tag color={step.success ? 'green' : 'red'}>#{step.iteration}</Tag>
                      {step.toolName ? (
                        <>
                          <Tag color="blue">{step.toolName}</Tag>
                          <span style={{ color: colors.textMuted }}>{step.result}</span>
                        </>
                      ) : (
                        <span>{step.result}</span>
                      )}
                    </div>
                  ))}
                </Space>
              </div>
            )}
          </Space>
        </Modal>
    </div>
  );
};

export default AgentsPage;
