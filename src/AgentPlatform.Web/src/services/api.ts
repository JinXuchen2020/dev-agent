import axios from 'axios';
import type {
  Agent,
  AgentRole,
  CreateAgentRequest,
  UpdateAgentRequest,
  AgentConfiguration,
  CreateAgentConfigurationRequest,
  UpdateAgentConfigurationRequest,
  ConfigurationAgentTemplate,
  Workflow,
  WorkflowDetail,
  ExecutionLog,
  ExecutionLogDetail,
  EvaluationDatasetSummary,
  EvaluationDatasetDetail,
  EvaluationReport,
  EvaluationMatchMode,
  ApiKey,
  Conversation,
  WorkflowNodeRequest,
  WorkflowEdgeRequest,
  WorkflowNodeRunResult,
  WorkflowVersionList,
  WorkflowVersionDetail,
  WorkflowExport,
  ImportWorkflowRequest,
  ApprovalDto,
  PublishStatus,
  PublishWorkflowRequest,
  KnowledgeBase,
  KnowledgeDocument,
  AuthUser,
  LoginRequest,
  LoginResponse,
  ResearchRequest,
  ResearchProgressEvent,
  CredentialCategory,
  TenantCredentialDto,
  CreateTenantCredentialRequest,
  UpdateTenantCredentialRequest,
  PlatformModelDto,
  ProviderModelInfo,
  DashboardSummary,
  WorkflowUsageList,
  WorkflowDiffDto,
  DiffWorkflowRequest,
  WorkflowTriggersResponse,
  ScheduleTriggerRequest,
  ScheduleTriggerView,
  WorkflowBindingDto,
  TriggerRunResult,
  WorkflowTemplate,
  WorkflowTemplateDetail,
  WorkflowTemplateCategory,
  WorkflowTemplateCategoryOption,
  StartDebugSessionResponse,
  DebugStepResponse,
  DebugResumeResponse,
  DebugRetryResponse,
  DebugVariablesResponse,
  DebugWorkflowStateSnapshot,
} from '../types';

const api = axios.create({
  baseURL: '/api/v1',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
  // Send the httpOnly auth cookie on every request (F2: cookie-based auth).
  withCredentials: true,
});

// No Authorization header injection: the auth cookie is sent automatically via
// `withCredentials`. JWT is never exposed to JavaScript (httpOnly).
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error?.response?.status;
    if (status === 401) {
      // Notify the router layer to redirect to /login inside the SPA
      // (no full-page reload, no unhandled rejection white screen).
      // Expected during an unauthenticated session check — do NOT log as error.
      if (typeof window !== 'undefined') {
        window.dispatchEvent(new CustomEvent('auth:unauthorized'));
      }
      return Promise.reject(error);
    }
    // Requests aborted mid-flight (e.g. SPA navigation away) are expected;
    // logging them only adds console noise.
    if (error?.code === 'ERR_CANCELED' || /canceled/i.test(error?.message ?? '')) {
      return Promise.reject(error);
    }
    console.error('API Error:', error.response?.data || error.message);
    return Promise.reject(error);
  },
);

// Agents
export const getAgents = () => api.get<Agent[]>('/agents').then((r) => r.data);
export const getAgent = (id: string) => api.get<Agent>(`/agents/${id}`).then((r) => r.data);
export const createAgent = (data: CreateAgentRequest) =>
  api.post<Agent>('/agents', data).then((r) => r.data);

export const updateAgent = (id: string, data: UpdateAgentRequest) =>
  api.put<Agent>(`/agents/${id}`, data).then((r) => r.data);

export const deleteAgent = (id: string) =>
  api.delete<void>(`/agents/${id}`).then(() => undefined);

// Agent Roles
export const getAgentRoles = () => api.get<AgentRole[]>('/agent-roles').then((r) => r.data);
export const getAgentRole = (roleCode: string) =>
  api.get<AgentRole>(`/agent-roles/${roleCode}`).then((r) => r.data);

export interface CreateAgentRoleRequest {
  name: string;
  roleCode: string;
  description?: string;
  systemPrompt: string;
}

export interface UpdateAgentRoleRequest {
  name: string;
  description?: string;
  systemPrompt: string;
}

export const createAgentRole = (req: CreateAgentRoleRequest) =>
  api.post<AgentRole>('/agent-roles', req).then((r) => r.data);

export const updateAgentRole = (roleCode: string, req: UpdateAgentRoleRequest) =>
  api.put<AgentRole>(`/agent-roles/${roleCode}`, req).then((r) => r.data);

export const deleteAgentRole = (roleCode: string) =>
  api.delete<void>(`/agent-roles/${roleCode}`).then(() => undefined);

// Agent Configurations
export const getAgentConfigurations = (opts?: {
  type?: string;
  skip?: number;
  take?: number;
  signal?: AbortSignal;
}) => {
  const { signal, ...params } = opts ?? {};
  return api
    .get<{ items: AgentConfiguration[]; totalCount: number }>('/agent-configurations', { params, signal })
    .then((r) => r.data);
};

export const getAgentConfiguration = (id: string) =>
  api.get<AgentConfiguration>(`/agent-configurations/${id}`).then((r) => r.data);

export const getAgentConfigurationTemplate = (id: string) =>
  api.get<ConfigurationAgentTemplate>(`/agent-configurations/${id}/template`).then((r) => r.data);

export const createAgentConfiguration = (req: CreateAgentConfigurationRequest) =>
  api.post<AgentConfiguration>('/agent-configurations', req).then((r) => r.data);

export const updateAgentConfiguration = (id: string, req: UpdateAgentConfigurationRequest) =>
  api.put<AgentConfiguration>(`/agent-configurations/${id}`, req).then((r) => r.data);

export const deleteAgentConfiguration = (id: string) =>
  api.delete<void>(`/agent-configurations/${id}`).then(() => undefined);

// Workflows
export const getWorkflows = (opts?: {
  status?: string | number;
  skip?: number;
  take?: number;
  signal?: AbortSignal;
}) => {
  const { signal, ...params } = opts ?? {};
  return api
    .get<{ items: Workflow[]; totalCount: number }>('/workflows', { params, signal })
    .then((r) => r.data);
};
export const getWorkflow = (id: string) =>
  api.get<WorkflowDetail>(`/workflows/${id}`).then((r) => r.data);
export const runWorkflow = (data: { name: string; initialContext: string; steps?: string[] }) =>
  api.post<Workflow>('/workflows', data).then((r) => r.data);
export const updateWorkflow = (
  id: string,
  data: {
    name?: string;
    initialContext?: string;
    steps?: string[];
    nodes?: WorkflowNodeRequest[];
    edges?: WorkflowEdgeRequest[];
  },
) => api.put<WorkflowDetail>(`/workflows/${id}`, data).then((r) => r.data);
export const runExistingWorkflow = (id: string, preset?: string) =>
  api.post<WorkflowDetail>(`/workflows/${id}/run`, preset ? { preset } : {}).then((r) => r.data);

// F20 S3 — HITL 人工审批门：列出某工作流全部审批记录（含待处理），解析（批准/拒绝）单个审批门。
// 路径不含 execId：审批按 workflowId 归并、由 approvalId 唯一定位（见 WorkflowsController）。
export const listWorkflowApprovals = (workflowId: string) =>
  api.get<ApprovalDto[]>(`/workflows/${workflowId}/approvals`).then((r) => r.data ?? []);

export const resolveApproval = (
  workflowId: string,
  approvalId: string,
  approved: boolean,
  input?: string | null,
) =>
  api
    .post<WorkflowDetail>(`/workflows/${workflowId}/approvals/${approvalId}/resolve`, {
      approved,
      input: input ?? null,
    })
    .then((r) => r.data);

// P1: run a single node for debugging; returns the node's new state/result.
export const runWorkflowNode = (id: string, nodeId: string) =>
  api
    .post<WorkflowNodeRunResult>(`/workflows/${id}/nodes/${nodeId}/run`)
    .then((r) => r.data);

// ── F25 Workflow Debugger ──
// 调试写操作后端限 Admin,Operator；读操作继承类级 [Authorize]。
export const startDebugSession = (workflowId: string, initialContext?: string) =>
  api
    .post<StartDebugSessionResponse>(`/workflows/${workflowId}/debug/run`, {
      initialContext: initialContext ?? null,
    })
    .then((r) => r.data);

export const resetDebugSession = (workflowId: string) =>
  api
    .post<StartDebugSessionResponse>(`/workflows/${workflowId}/debug/reset`)
    .then((r) => r.data);

export const debugStep = (workflowId: string, sessionId: string) =>
  api
    .post<DebugStepResponse>(`/workflows/${workflowId}/debug/step`, { sessionId })
    .then((r) => r.data);

export const debugResume = (workflowId: string, sessionId: string) =>
  api
    .post<DebugResumeResponse>(`/workflows/${workflowId}/debug/resume`, { sessionId })
    .then((r) => r.data);

export const debugRetryNode = (
  workflowId: string,
  sessionId: string,
  nodeId: string,
  overriddenConfig?: string,
) =>
  api
    .post<DebugRetryResponse>(`/workflows/${workflowId}/debug/retry-node`, {
      sessionId,
      nodeId,
      overriddenConfig: overriddenConfig ?? null,
    })
    .then((r) => r.data);

export const debugRollback = (workflowId: string, sessionId: string, targetStepOrder: number) =>
  api
    .post<DebugResumeResponse>(`/workflows/${workflowId}/debug/rollback`, {
      sessionId,
      targetStepOrder,
    })
    .then((r) => r.data);

export const getDebugState = (workflowId: string) =>
  api.get<DebugWorkflowStateSnapshot>(`/workflows/${workflowId}/debug/state`).then((r) => r.data);

export const getDebugVariables = (workflowId: string, sessionId: string) =>
  api
    .get<DebugVariablesResponse>(`/workflows/${workflowId}/debug/variables`, {
      params: { sessionId },
    })
    .then((r) => r.data);

// F7 工作流版本管理 + 导入导出
export const getWorkflowVersions = (workflowId: string, opts?: { skip?: number; take?: number }) =>
  api
    .get<WorkflowVersionList>(`/workflows/${workflowId}/versions`, { params: opts })
    .then((r) => r.data);

export const getWorkflowVersion = (workflowId: string, versionId: string) =>
  api.get<WorkflowVersionDetail>(`/workflows/${workflowId}/versions/${versionId}`).then((r) => r.data);

export const createWorkflowVersion = (workflowId: string, note?: string | null) =>
  api
    .post<WorkflowVersionDetail>(`/workflows/${workflowId}/versions`, { note: note ?? null })
    .then((r) => r.data);

export const restoreWorkflowVersion = (workflowId: string, versionId: string) =>
  api
    .post<WorkflowDetail>(`/workflows/${workflowId}/versions/${versionId}/restore`)
    .then((r) => r.data);

export const deleteWorkflowVersion = (workflowId: string, versionId: string) =>
  api.delete<void>(`/workflows/${workflowId}/versions/${versionId}`).then(() => undefined);

// 导出当前工作流定义为 JSON（WorkflowExport，可直接回灌 importWorkflow）。
export const exportWorkflow = (workflowId: string) =>
  api.get<WorkflowExport>(`/workflows/${workflowId}/export`).then((r) => r.data);

// 从 JSON 定义导入为「新」工作流，返回新建的 WorkflowDetail。
export const importWorkflow = (req: ImportWorkflowRequest) =>
  api.post<WorkflowDetail>(`/workflows/import`, req).then((r) => r.data);

// F22 · 发布工作流为 API / MCP 端点（管理面）。
// 未发布时 GET 返回 204（无内容），此处归一化为 null。
export const getPublishStatus = (workflowId: string) =>
  api
    .get<PublishStatus | null>(`/workflows/${workflowId}/publish`)
    .then((r) => (r.status === 204 ? null : r.data ?? null));

export const publishWorkflow = (workflowId: string, req: PublishWorkflowRequest) =>
  api.post<PublishStatus>(`/workflows/${workflowId}/publish`, req).then((r) => r.data);

export const unpublishWorkflow = (workflowId: string) =>
  api.delete<void>(`/workflows/${workflowId}/publish`).then(() => undefined);
// F21 工作流触发器（Webhook / 定时 / Chat）
// 管理端点受 RBAC Admin,Operator 保护；查询端点仅需登录。
export const generateWebhookToken = (workflowId: string) =>
  api
    .post<{ triggerToken: string; created: boolean }>(`/workflows/${workflowId}/triggers/webhook`)
    .then((r) => r.data);

export const disableWebhookTrigger = (workflowId: string) =>
  api
    .delete<{ enabled: boolean }>(`/workflows/${workflowId}/triggers/webhook`)
    .then((r) => r.data);

export const putScheduleTrigger = (workflowId: string, req: ScheduleTriggerRequest) =>
  api
    .put<ScheduleTriggerView>(`/workflows/${workflowId}/triggers/schedule`, req)
    .then((r) => r.data);

export const getWorkflowTriggers = (workflowId: string) =>
  api.get<WorkflowTriggersResponse>(`/workflows/${workflowId}/triggers`).then((r) => r.data);

// F21 Chat 触发器：会话 ↔ 工作流绑定与触发（仅需登录，受租户隔离）。
export const listConversationWorkflowBindings = (conversationId: string) =>
  api
    .get<WorkflowBindingDto[]>(`/conversations/${conversationId}/workflow-bindings`)
    .then((r) => r.data ?? []);

export const bindWorkflow = (conversationId: string, workflowId: string) =>
  api
    .post<{ id: string }>(`/conversations/${conversationId}/workflow-bindings`, { workflowId })
    .then((r) => r.data);

export const unbindWorkflow = (conversationId: string, workflowId: string) =>
  api
    .delete<void>(`/conversations/${conversationId}/workflow-bindings/${workflowId}`)
    .then(() => undefined);

export const triggerWorkflowFromConversation = (conversationId: string, workflowId: string) =>
  api
    .post<TriggerRunResult>(`/conversations/${conversationId}/trigger-workflow/${workflowId}`)
    .then((r) => r.data);

// Execution Logs
export const getExecutionLogs = (opts?: {
  status?: string | number;
  from?: string;
  to?: string;
  skip?: number;
  take?: number;
  signal?: AbortSignal;
}) => {
  const { signal, ...params } = opts ?? {};
  return api
    .get<{ items: ExecutionLog[]; totalCount: number }>('/execution-logs', { params, signal })
    .then((r) => r.data);
};
export const getExecutionLogDetail = (id: string) =>
  api.get<ExecutionLogDetail>(`/execution-logs/${id}`).then((r) => r.data);
export const getExecutionLogSteps = (id: string, params?: { status?: string; skip?: number; take?: number }) =>
  api.get<{ items: ExecutionLogDetail['entries']; totalCount: number }>(`/execution-logs/${id}/steps`, { params }).then((r) => r.data);

// ── 评估数据集 / 回归评估（F24）──
export const getEvaluationDatasets = (keyword?: string) =>
  api.get<EvaluationDatasetSummary[]>('/evaluation-datasets', { params: keyword ? { keyword } : undefined }).then((r) => r.data);

export const getEvaluationDataset = (id: string) =>
  api.get<EvaluationDatasetDetail>(`/evaluation-datasets/${id}`).then((r) => r.data);

export const createEvaluationDataset = (req: {
  name: string;
  description?: string | null;
  cases: { input: string; expectedOutput: string; matchMode: EvaluationMatchMode }[];
}) => api.post<EvaluationDatasetDetail>('/evaluation-datasets', req).then((r) => r.data);

export const updateEvaluationDataset = (
  id: string,
  req: {
    name: string;
    description?: string | null;
    cases: { input: string; expectedOutput: string; matchMode: EvaluationMatchMode }[];
  },
) => api.put<EvaluationDatasetDetail>(`/evaluation-datasets/${id}`, req).then((r) => r.data);

export const deleteEvaluationDataset = (id: string) =>
  api.delete(`/evaluation-datasets/${id}`).then((r) => r.data);

export const runEvaluation = (id: string, workflowId: string) =>
  api.post<EvaluationReport>(`/evaluation-datasets/${id}/run`, { workflowId }).then((r) => r.data);

export default api;

// API Keys
export const getApiKeys = () => api.get<ApiKey[]>('/api-keys').then((r) => r.data);

// Conversations (F3 extension: server-side status + q filtering)
export const getConversations = (params?: {
  status?: number | string;
  q?: string;
  signal?: AbortSignal;
}) => {
  const { status, q, signal } = params ?? {};
  return api
    .get<Conversation[]>('/conversations', { params: { status, q }, signal })
    .then((r) => r.data);
};
export const createConversation = () =>
  api.post<Conversation>('/conversations', {}).then((r) => r.data);
export const getConversation = (id: string) =>
  api.get<Conversation>(`/conversations/${id}`).then((r) => r.data);
export const setConversationKnowledgeBase = (id: string, knowledgeBaseId: string) =>
  api
    .put<{ id: string }>(`/conversations/${id}/knowledge-base`, { knowledgeBaseId })
    .then((r) => r.data);
export const removeConversationKnowledgeBase = (id: string) =>
  api.delete<{ id: string }>(`/conversations/${id}/knowledge-base`).then(() => undefined);

export interface SendMessageOptions {
  searchQuery?: string;
  model?: string;
}
export const sendMessage = (
  id: string,
  content: string,
  options?: SendMessageOptions,
) =>
  api
    .post<{ reply: string; modelId: string; tokenUsage?: { promptTokens: number; completionTokens: number } }>(
      `/conversations/${id}/messages`,
      { content, searchQuery: options?.searchQuery, model: options?.model },
    )
    .then((r) => r.data);

// Knowledge Bases (RAG 地基层 R1-R4)
export const getKnowledgeBases = (signal?: AbortSignal) =>
  api.get<KnowledgeBase[]>('/knowledge-bases', { signal }).then((r) => r.data);
export const getKnowledgeBase = (id: string) =>
  api.get<KnowledgeBase>(`/knowledge-bases/${id}`).then((r) => r.data);
export const createKnowledgeBase = (data: {
  name: string;
  description?: string | null;
  embeddingModel?: string | null;
}) => api.post<KnowledgeBase>('/knowledge-bases', data).then((r) => r.data);
export const deleteKnowledgeBase = (id: string) =>
  api.delete<void>(`/knowledge-bases/${id}`).then(() => undefined);
export const uploadDocument = (id: string, file: File) => {
  const form = new FormData();
  form.append('file', file);
  return api
    .post<KnowledgeDocument>(`/knowledge-bases/${id}/documents`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    .then((r) => r.data);
};

// F23 · 模板市场（平台级工作流模板，只读 + 一键克隆）。
// 列表/分类/详情仅需登录；克隆为 Admin,Operator（后端 [Authorize(Roles=...)]）。
// params 仅包含非 null 键，避免 axios 将 null 序列化为 "null" 字符串导致后端误过滤（空列表）。
export const getWorkflowTemplates = (opts?: {
  category?: WorkflowTemplateCategory | null;
  keyword?: string | null;
}) => {
  const params: Record<string, string | number> = {};
  if (opts?.category != null) params.category = opts.category;
  if (opts?.keyword) params.keyword = opts.keyword;
  return api.get<WorkflowTemplate[]>('/workflow-templates', { params }).then((r) => r.data ?? []);
};

export const getWorkflowTemplateCategories = () =>
  api.get<WorkflowTemplateCategoryOption[]>('/workflow-templates/categories').then((r) => r.data ?? []);

export const getWorkflowTemplate = (id: string) =>
  api.get<WorkflowTemplateDetail>(`/workflow-templates/${id}`).then((r) => r.data);

// 克隆后返回新工作流的详情（WorkflowDetail），可直接跳转到 /workflows/:id。
export const cloneWorkflowTemplate = (id: string) =>
  api.post<WorkflowDetail>(`/workflow-templates/${id}/clone`).then((r) => r.data);

// Auth (F2: cookie-based; identity via GET /auth/me, no client-side JWT decode)
export const loginRequest = (data: LoginRequest) =>
  api.post<LoginResponse>('/auth/login', data).then((r) => r.data);

export const getAuthMe = () =>
  api.get<AuthUser>('/auth/me').then((r) => r.data);

export const logoutRequest = () =>
  api.post<void>('/auth/logout').then(() => undefined);

// F13 多租户凭据（模型 + 搜索，BYO-Key + 平台内置）。
// 一个租户可配置多个同类凭据，统一以列表返回（可能为空数组）。
export const getTenantCredentials = (category: CredentialCategory) =>
  api
    .get<TenantCredentialDto[]>('/tenant/credentials', { params: { category } })
    .then((r) => r.data ?? []);

export const createTenantCredential = (req: CreateTenantCredentialRequest) =>
  api.post<TenantCredentialDto>('/tenant/credentials', req).then((r) => r.data);

export const updateTenantCredential = (req: UpdateTenantCredentialRequest) =>
  api.put<TenantCredentialDto>(`/tenant/credentials/${req.id}`, req).then((r) => r.data);

export const deleteTenantCredential = (id: string) =>
  api.delete<void>(`/tenant/credentials/${id}`).then(() => undefined);

// 平台模型目录（platform-* + 当前租户 BYO 模型并列），不含密钥。
export const getPlatformModels = () =>
  api.get<PlatformModelDto[]>('/models').then((r) => r.data);

// F14 供应商模型发现：填 Key + Base URL 后，拉取该 provider 账户下所有可访问模型清单。
// 密钥仅用于本次一次性探测，不落库、不回显。
export const discoverProviderModels = (req: {
  provider: string;
  apiKey: string;
  baseUrl?: string | null;
}) =>
  api
    .post<ProviderModelInfo[]>('/tenant/credentials/discover-models', req)
    .then((r) => r.data ?? []);

// F18 Dashboard analytics（GET /analytics/summary）。
// 不传 from/to 时后端默认返回最近 14 天；前端范围选择器主动传 from=now-N天, to=now。
export const getDashboardSummary = (opts?: {
  from?: string;
  to?: string;
  signal?: AbortSignal;
}) => {
  const { signal, ...params } = opts ?? {};
  return api
    .get<DashboardSummary>('/analytics/summary', { params, signal })
    .then((r) => r.data);
};

// F26 工作流用量（GET /analytics/workflows）。按工作流聚合执行数 / 成功率 / 延迟 / Token。
// 不传 from/to 时后端默认返回最近 14 天；范围上限 366 天。
export const getWorkflowUsage = (opts?: {
  from?: string;
  to?: string;
  signal?: AbortSignal;
}) => {
  const { signal, ...params } = opts ?? {};
  return api
    .get<WorkflowUsageList>('/analytics/workflows', { params, signal })
    .then((r) => r.data);
};

// F26 工作流定义 diff（POST /workflows/{id}/diff）。只读查询，对比两个工作流定义
// （版本对 / 某版本 vs 当前 / 另一工作流 / 当前 vs 最新版本）。返回结构化增删改。
export const diffWorkflow = (workflowId: string, req: DiffWorkflowRequest) =>
  api
    .post<WorkflowDiffDto>(`/workflows/${workflowId}/diff`, req)
    .then((r) => r.data);

// Normalize an unknown thrown value into a human-readable message.
// Preserves axios-style `response.data.title` / `response.data.message` when present,
// and also handles a plain-string response body (e.g. backend 400 with a raw message).
export function getErrorMessage(e: unknown): string {
  if (e && typeof e === 'object') {
    const err = e as {
      response?: { data?: { title?: string; message?: string } | string };
      message?: string;
    };
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) {
      return data;
    }
    if (data && typeof data === 'object') {
      const d = data as { title?: string; message?: string };
      return d.title ?? d.message ?? err.message ?? '未知错误';
    }
    return err.message ?? '未知错误';
  }
  return String(e);
}

// Research Agent (F6: 联网多步调研). The endpoint streams Server-Sent Events, but
// EventSource only supports GET, so we use fetch + a manual SSE frame parser.
// Each `data:` frame is a JSON-encoded ResearchProgressEvent. The terminal frame is
// `event: done` with an empty `data: {}`, which we ignore.
export async function runResearch(
  req: ResearchRequest,
  onEvent: (e: ResearchProgressEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch('/api/v1/research', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(req),
    signal,
  });

  if (!res.ok || !res.body) {
    let detail = `HTTP ${res.status}`;
    try {
      const text = await res.text();
      if (text) detail = text;
    } catch {
      /* ignore body read failure */
    }
    throw new Error(detail);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let sep: number;
    while ((sep = buffer.indexOf('\n\n')) >= 0) {
      const frame = buffer.slice(0, sep);
      buffer = buffer.slice(sep + 2);
      const dataLine = frame
        .split('\n')
        .find((line) => line.startsWith('data:'));
      if (!dataLine) continue;
      const json = dataLine.slice(5).trim();
      if (!json || json === '{}') continue; // terminal `event: done` frame
      try {
        onEvent(JSON.parse(json) as ResearchProgressEvent);
      } catch {
        /* ignore malformed frame */
      }
    }
  }
}
