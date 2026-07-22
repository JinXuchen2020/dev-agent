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
} from '../types';

const api = axios.create({
  baseURL: '/api/v1',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
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
export const updateWorkflow = (id: string, data: { name?: string; initialContext?: string; steps?: string[] }) =>
  api.put<WorkflowDetail>(`/workflows/${id}`, data).then((r) => r.data);
export const runExistingWorkflow = (id: string, preset?: string) =>
  api.post<WorkflowDetail>(`/workflows/${id}/run`, preset ? { preset } : {}).then((r) => r.data);

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
