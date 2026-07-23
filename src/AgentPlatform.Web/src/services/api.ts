import axios from 'axios';
import type {
  Agent,
  AgentRole,
  AgentConfiguration,
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
} from '../types';

const api = axios.create({
  baseURL: '/api/v1',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = getAuthToken();
  if (token) {
    if (config.headers && typeof (config.headers as { set?: unknown }).set === 'function') {
      (config.headers as { set: (k: string, v: string) => void }).set(
        'Authorization',
        `Bearer ${token}`,
      );
    } else {
      config.headers = { ...(config.headers as object), Authorization: `Bearer ${token}` } as typeof config.headers;
    }
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    console.error('API Error:', error.response?.data || error.message);
    return Promise.reject(error);
  },
);

// Agents
export const getAgents = () => api.get<Agent[]>('/agents').then((r) => r.data);
export const getAgent = (id: string) => api.get<Agent>(`/agents/${id}`).then((r) => r.data);

// Agent Roles
export const getAgentRoles = () => api.get<AgentRole[]>('/agent-roles').then((r) => r.data);
export const getAgentRole = (roleCode: string) =>
  api.get<AgentRole>(`/agent-roles/${roleCode}`).then((r) => r.data);

// Agent Configurations
export const getAgentConfigurations = (params?: { type?: string; skip?: number; take?: number }) =>
  api.get<{ items: AgentConfiguration[]; totalCount: number }>('/agent-configurations', { params }).then((r) => r.data);

// Workflows
export const getWorkflows = (params?: { status?: string; skip?: number; take?: number }) =>
  api.get<{ items: Workflow[]; totalCount: number }>('/workflows', { params }).then((r) => r.data);
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
export const getExecutionLogs = (params?: {
  status?: string;
  from?: string;
  to?: string;
  skip?: number;
  take?: number;
}) => api.get<{ items: ExecutionLog[]; totalCount: number }>('/execution-logs', { params }).then((r) => r.data);
export const getExecutionLogDetail = (id: string) =>
  api.get<ExecutionLogDetail>(`/execution-logs/${id}`).then((r) => r.data);
export const getExecutionLogSteps = (id: string, params?: { status?: string; skip?: number; take?: number }) =>
  api.get<{ items: ExecutionLogDetail['entries']; totalCount: number }>(`/execution-logs/${id}/steps`, { params }).then((r) => r.data);

export default api;

// Auth (dev/demo login; backend DevLoginEnabled gate)
export const devLogin = (data: { role: string; userId: string }) =>
  api.post<{ token: string }>('/auth/dev-login', data).then((r) => r.data);

// API Keys
export const getApiKeys = () => api.get<ApiKey[]>('/api-keys').then((r) => r.data);

// Conversations
export const getConversations = () => api.get<Conversation[]>('/conversations').then((r) => r.data);
export const createConversation = () =>
  api.post<Conversation>('/conversations', {}).then((r) => r.data);
export const getConversation = (id: string) =>
  api.get<Conversation>(`/conversations/${id}`).then((r) => r.data);
export const setConversationKnowledgeBase = (id: string, knowledgeBaseId: string) =>
  api
    .put<{ id: string }>(`/conversations/${id}/knowledge-base`, { knowledgeBaseId })
    .then((r) => r.data);
export const removeConversationKnowledgeBase = (id: string) =>
  api.delete<{ id: string }>(`/conversations/${id}/knowledge-base`).then((r) => r.data);

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
export const getKnowledgeBases = () =>
  api.get<KnowledgeBase[]>('/knowledge-bases').then((r) => r.data);
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

// Centralized auth token accessor (avoids duplicating the storage key literal).
export function getAuthToken(): string | null {
  return typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
}

// Normalize an unknown thrown value into a human-readable message.
// Preserves axios-style `response.data.title` / `response.data.message` when present.
export function getErrorMessage(e: unknown): string {
  if (e && typeof e === 'object') {
    const err = e as {
      response?: { data?: { title?: string; message?: string } };
      message?: string;
    };
    return err.response?.data?.title ?? err.response?.data?.message ?? err.message ?? '未知错误';
  }
  return String(e);
}

// 客户端解码 JWT payload（仅用于展示真实身份，不做签名校验；后端仍是鉴权权威）。
// 后端 dev-login 令牌声明：sub/name（= 邮箱）、role（见 DevLoginEndpoint.cs:35-39；无 tenant_id）。
export function decodeJwt(token: string): Record<string, string | undefined> | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64);
    const bytes = Uint8Array.from(bin, (c) => c.charCodeAt(0));
    const json = new TextDecoder().decode(bytes);
    return JSON.parse(json) as Record<string, string | undefined>;
  } catch {
    return null;
  }
}

