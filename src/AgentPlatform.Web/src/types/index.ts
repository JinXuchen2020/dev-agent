export interface Agent {
  id: string;
  name: string;
  role: { roleCode: string };
  modelEndpoint?: { modelId: string };
  systemPrompt: string;
  status: string;
  createdAt: string;
}

export interface CreateAgentRequest {
  name: string;
  roleCode?: string | null;
  modelProvider?: string | null;
  modelName?: string | null;
  modelApiUrl?: string | null;
  systemPrompt?: string | null;
}

export interface ApiKey {
  id: string;
  name: string;
  prefix: string;
  role: string;
  expiresAt: string;
  lastUsedAt: string | null;
  status: string;
}

export interface Conversation {
  id: string;
  agentName?: string;
  workflowId?: string;
  knowledgeBaseId?: string;
  collectionName?: string;
  messages?: { role: string; content: string }[];
  status?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface AgentRole {
  id: string;
  name: string;
  roleCode: string;
  description: string;
  systemPrompt: string;
  capabilities?: string[];
  isActive: boolean;
}

export interface AgentConfiguration {
  id: string;
  name: string;
  agentType: string;
  version: string;
  yamlContent: string;
  isActive: boolean;
  createdAt: string;
}

export interface Workflow {
  id: string;
  name: string;
  currentState: string;
  stepCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface WorkflowStep {
  id: string;
  order: number;
  stepName: string;
  assignedAgentId: string | null;
  state: string;
  result: string | null;
  errorDetail: string | null;
}

// P1 DAG: StepType must match AgentPlatform.Domain.Enums.StepType (serialized as int).
// Declared as a const object (not `enum`) because tsconfig enables `erasableSyntaxOnly`.
export const StepType = {
  Start: 0,
  End: 1,
  LLM: 2,
  Agent: 3,
  Critic: 4,
  Knowledge: 5,
  Tool: 6,
  Code: 7,
} as const;
export type StepType = (typeof StepType)[keyof typeof StepType];

// Parsed node configuration (stored as JSON string in `configJson` on the backend).
export interface NodeConfig {
  systemPrompt?: string;
  agentId?: string | null;
  criteria?: string;
  summary?: string;
  initialContext?: string;
  knowledgeBaseId?: string | null;
  query?: string;
  // 工具调用节点 (StepType.Tool)
  toolName?: string;
  parameters?: string;
  // 代码执行节点 (StepType.Code)
  code?: string;
  language?: string;
}

// Backend response: a single graph node.
export interface WorkflowNodeResponse {
  id: string;
  type: StepType;
  name: string;
  order: number;
  positionX: number;
  positionY: number;
  configJson: string;
  state: string;
  result: string | null;
  errorDetail: string | null;
  assignedAgentId: string | null;
}

// Backend response: a single graph edge.
export interface WorkflowEdgeResponse {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  label: string | null;
}

// Backend request: a single graph node.
export interface WorkflowNodeRequest {
  id: string;
  type: StepType;
  name: string;
  position: { x: number; y: number };
  config?: string | null;
  assignedAgentId?: string | null;
}

// Backend request: a single graph edge.
export interface WorkflowEdgeRequest {
  id: string;
  source: string;
  target: string;
  label?: string | null;
}

// Backend response: result of a single-node debug run.
export interface WorkflowNodeRunResult {
  nodeId: string;
  state: string;
  result: string | null;
  errorDetail: string | null;
}

export interface WorkflowDetail {
  id: string;
  name: string;
  currentState: string;
  steps: WorkflowStep[];
  nodes: WorkflowNodeResponse[];
  edges: WorkflowEdgeResponse[];
  context: string;
  createdAt: string;
  updatedAt: string;
}

export interface ExecutionLog {
  id: string;
  workflowId: string;
  workflowName: string;
  status: string;
  totalSteps: number;
  completedSteps: number;
  failedSteps: number;
  startedAt: string;
  completedAt: string | null;
}

export interface ExecutionLogDetail extends ExecutionLog {
  entries: ExecutionLogStepEntry[];
}

export interface ExecutionLogStepEntry {
  id: string;
  stepName: string;
  stepOrder: number;
  status: string;
  duration: string;
  result: string | null;
  errorDetail: string | null;
  startedAt: string;
  completedAt: string;
}

// ── RAG 知识库（R1-R4 地基层）──
export interface KnowledgeDocument {
  id: string;
  documentId: string;
  fileName: string;
  contentType: string;
  chunkCount: number;
  createdAt: string;
}

export interface KnowledgeBase {
  id: string;
  name: string;
  description: string;
  collectionName: string;
  embeddingModel: string;
  createdAt: string;
  documents: KnowledgeDocument[];
}

// ── Auth (F2: cookie-based auth; identity comes from GET /auth/me) ──
export interface AuthUser {
  id: string;
  email: string;
  role: string;
  tenantId: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  user: AuthUser;
}

// ── Research Agent (F6: 联网多步调研) ──
// Backend serializes the event `Type` as a numeric enum (System.Text.Json default),
// matching the rest of the API's int-enum convention. Mirror it with a const map.
export const ResearchEventTypeValue = {
  Plan: 0,
  SearchStart: 1,
  SearchDone: 2,
  Synthesize: 3,
  Report: 4,
  Error: 5,
} as const;
export type ResearchEventTypeValue =
  (typeof ResearchEventTypeValue)[keyof typeof ResearchEventTypeValue];

export interface ResearchSource {
  title: string;
  url: string;
  snippet: string;
}

export interface ResearchSection {
  heading: string;
  body: string;
}

export interface ResearchTokenUsage {
  promptTokens: number;
  completionTokens: number;
}

export interface ResearchReport {
  question: string;
  searchQueries: string[];
  sources: ResearchSource[];
  answer: string;
  sections: ResearchSection[];
  stepsUsed: number;
  tokenUsage: ResearchTokenUsage | null;
  generatedAt: string;
}

export interface ResearchProgressEvent {
  type: ResearchEventTypeValue;
  message?: string | null;
  queries?: string[] | null;
  query?: string | null;
  snippetCount?: number | null;
  report?: ResearchReport | null;
  error?: string | null;
}

export interface ResearchRequest {
  question: string;
  maxSteps?: number | null;
  modelId?: string | null;
  focusInstructions?: string | null;
}

// ── F13 多租户凭据配置（模型 + 搜索，BYO-Key + 平台内置）──
// 与 AgentPlatform.Domain.Enums.CredentialCategory 对齐（序列化为 int）。
export const CredentialCategory = {
  Model: 0,
  Search: 1,
} as const;
export type CredentialCategory =
  (typeof CredentialCategory)[keyof typeof CredentialCategory];

export interface TenantCredentialDto {
  category: CredentialCategory;
  provider: string;
  apiKeyMask: string;
  baseUrl: string | null;
  modelName: string | null;
  isEnabled: boolean;
}

export interface UpdateTenantCredentialRequest {
  category: CredentialCategory;
  provider: string;
  apiKey?: string | null;
  baseUrl?: string | null;
  modelName?: string | null;
  isEnabled?: boolean;
}

export interface PlatformModelDto {
  modelId: string;
  provider: string;
  displayName: string;
  isTenantOwned: boolean;
}
