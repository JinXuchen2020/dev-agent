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
  ApiKey,
  Conversation,
  WorkflowNodeRequest,
  WorkflowEdgeRequest,
  WorkflowNodeRunResult,
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

// P1: run a single node for debugging; returns the node's new state/result.
export const runWorkflowNode = (id: string, nodeId: string) =>
  api
    .post<WorkflowNodeRunResult>(`/workflows/${id}/nodes/${nodeId}/run`)
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
