export interface Agent {
  id: string;
  name: string;
  roleCode: string;
  modelProvider?: string | null;
  modelName?: string | null;
  tenantId: string;
  status?: string;
  systemPrompt: string;
  createdAt: string;
}

export interface CreateAgentRequest {
  name: string;
  roleCode?: string | null;
  modelProvider?: string | null;
  modelName?: string | null;
  modelApiUrl?: string | null;
  systemPrompt?: string | null;
  /** Optional id of the source agent configuration this agent was instantiated from (provenance only). */
  configurationId?: string | null;
}

// PATCH-style update: all fields optional; backend applies only the supplied ones.
export interface UpdateAgentRequest {
  name?: string | null;
  roleCode?: string | null;
  modelProvider?: string | null;
  modelName?: string | null;
  modelApiUrl?: string | null;
  systemPrompt?: string | null;
  status?: string | null;
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
  isBuiltIn: boolean;
  agentCount: number;
}

// AgentConfigurationStatus mirrors AgentPlatform.Domain.Enums.AgentConfigurationStatus
// (serialized as int by System.Text.Json). Used to render status tags on the config library.
export const AgentConfigurationStatus = {
  Draft: 0,
  Active: 1,
  Archived: 2,
  Deprecated: 3,
} as const;
export type AgentConfigurationStatus =
  (typeof AgentConfigurationStatus)[keyof typeof AgentConfigurationStatus];

// Backend serializes AgentConfigurationSummary / AgentConfigurationResponse with camelCase
// (agentTypeCode, status, updatedAt), NOT the legacy agentType / isActive / createdAt.
export interface AgentConfiguration {
  id: string;
  name: string;
  description?: string | null;
  agentTypeCode?: string | null;
  version: string;
  /** AgentConfigurationStatus enum value (0 Draft, 1 Active, 2 Archived, 3 Deprecated). */
  status: AgentConfigurationStatus;
  createdAt?: string;
  updatedAt: string;
  /** Present only on the detail endpoint (GET /agent-configurations/{id}), not on the list. */
  yamlContent?: string;
  tenantId?: string;
}

export interface CreateAgentConfigurationRequest {
  name: string;
  yamlContent: string;
  description?: string | null;
  agentTypeCode?: string | null;
}

export interface UpdateAgentConfigurationRequest {
  yamlContent: string;
  changeLog?: string | null;
  /** VersionBump enum: 0 Patch, 1 Minor, 2 Major. */
  versionBump?: number;
  name?: string | null;
  description?: string | null;
}

// Structured projection returned by GET /agent-configurations/{id}/template.
export interface ConfigurationAgentTemplate {
  configurationId: string;
  name: string;
  description?: string | null;
  roleCode?: string | null;
  modelProvider?: string | null;
  modelName?: string | null;
  modelApiUrl?: string | null;
  systemPrompt?: string | null;
  sourceVersion: string;
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
// F20：新增 HTTP/Condition/Loop/Variable/SubWorkflow/Delay/UserInput（值 8–14 与后端一致）。
export const StepType = {
  Start: 0,
  End: 1,
  LLM: 2,
  Agent: 3,
  Critic: 4,
  Knowledge: 5,
  Tool: 6,
  Code: 7,
  Http: 8,
  Condition: 9,
  Loop: 10,
  Variable: 11,
  SubWorkflow: 12,
  Delay: 13,
  UserInput: 14,
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
  // ── F20 节点类型配置 ──
  // HTTP 节点 (StepType.Http)
  method?: string;
  url?: string;
  headers?: string; // JSON 对象字符串，如 {"Authorization":"Bearer ..."}
  bodyTemplate?: string;
  authRef?: string;
  // 条件节点 (StepType.Condition)
  expression?: string; // Jint 沙箱表达式，可引用 artifacts/blackboard/input/Math
  // 循环节点 (StepType.Loop)
  itemsSource?: string; // JSON 数组字符串或 Blackboard/Artifact 键名
  itemVariable?: string; // 每轮注入共享 Blackboard 的变量名
  bodyNodeNames?: string[]; // 引用主图节点名列表（构成循环体）
  // 变量节点 (StepType.Variable)
  mode?: 'set' | 'get';
  name?: string; // 变量名（Blackboard 键）
  value?: string; // set 模式的值（支持 {{占位}}）
  // 子工作流节点 (StepType.SubWorkflow)
  workflowId?: string; // 目标工作流 Id（GUID）
  inputMapping?: string; // 可选输入映射 JSON
  // 延迟节点 (StepType.Delay)
  durationMs?: number;
  // 人工审批门节点 (StepType.UserInput)
  prompt?: string; // 展示给审批人的提示
  approvalRole?: string; // 可选审批角色
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

// F20 S3 — HITL 人工审批门（与 HumanApprovalDto 字段镜像；Status 为整数枚举：
// Pending=0 / Approved=1 / Rejected=2，见 AgentPlatform.Domain.Enums.HumanApprovalStatus）。
export interface ApprovalDto {
  id: string;
  workflowId: string;
  nodeName: string;
  prompt: string;
  status: number;
  submittedInput: string | null;
  resolvedAt: string | null;
  createdAt: string;
  executionId: string | null;
}

// ── F7 工作流版本管理 + 导入导出 ──
// 字段名镜像 AgentPlatform.Application.Workflows.Versioning.*（System.Text.Json 默认 camelCase）。

// 版本快照内的单个节点（无运行时 state/result）。
export interface WorkflowVersionNodeView {
  id: string;
  type: StepType;
  name: string;
  x: number;
  y: number;
  configJson: string | null;
  assignedAgentId: string | null;
}

// 版本快照内的单条边。
export interface WorkflowVersionEdgeView {
  id: string;
  source: string;
  target: string;
  label: string | null;
}

export interface WorkflowVersionSummary {
  id: string;
  versionNumber: number;
  name: string;
  note: string | null;
  createdAt: string;
  createdBy: string | null;
}

export interface WorkflowVersionDetail {
  id: string;
  versionNumber: number;
  name: string;
  note: string | null;
  createdAt: string;
  createdBy: string | null;
  context: string;
  nodes: WorkflowVersionNodeView[];
  edges: WorkflowVersionEdgeView[];
}

export interface WorkflowVersionList {
  items: WorkflowVersionSummary[];
  totalCount: number;
}

// 导出 = 与导入请求同构（nodes/edges 可直接回灌 importWorkflow）。
export interface WorkflowExport {
  id: string;
  name: string;
  context: string;
  nodes: WorkflowNodeRequest[];
  edges: WorkflowEdgeRequest[];
  exportedAt: string;
}

export interface ImportWorkflowRequest {
  name: string;
  initialContext: string;
  nodes?: WorkflowNodeRequest[] | null;
  edges?: WorkflowEdgeRequest[] | null;
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
  id: string;
  name: string;
  category: CredentialCategory;
  provider: string;
  apiKeyMask: string;
  baseUrl: string | null;
  modelName: string | null;
  isEnabled: boolean;
}

export interface CreateTenantCredentialRequest {
  category: CredentialCategory;
  name: string;
  provider: string;
  apiKey: string;
  baseUrl?: string | null;
  modelName?: string | null;
  isEnabled?: boolean;
}

export interface UpdateTenantCredentialRequest {
  id: string;
  name: string;
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

// F14 供应商模型发现：填 Key + Base URL 后拉取的 provider 账户可访问模型清单项。
export interface ProviderModelInfo {
  id: string;
  ownedBy?: string | null;
}

// ── F18 Dashboard analytics（GET /analytics/summary）──
// 字段名镜像 AgentPlatform.Application.Analytics.Queries.GetDashboardSummary，
// 后端 System.Text.Json 默认 camelCase 序列化。
export interface DashboardKpis {
  activeAgents: number;
  activeWorkflows: number;
  totalExecutions: number;
  /** 成功率（%），已终态执行中 completed / (completed + failed)。 */
  successRate: number;
  totalTokens: number;
  /** 平均单执行步延迟（ms）。 */
  avgLatencyMs: number;
}

export interface ExecutionDayBucket {
  date: string;
  completed: number;
  failed: number;
  running: number;
  successRate: number;
}

export interface TokenDayBucket {
  date: string;
  totalTokens: number;
}

export interface ConversationDayBucket {
  date: string;
  count: number;
}

export interface LatencyDayBucket {
  date: string;
  avgMs: number;
}

export interface WorkflowCount {
  workflowName: string;
  count: number;
}

export interface DashboardSummary {
  from: string;
  to: string;
  kpis: DashboardKpis;
  executionsByDay: ExecutionDayBucket[];
  tokenByDay: TokenDayBucket[];
  conversationsByDay: ConversationDayBucket[];
  latencyByDay: LatencyDayBucket[];
  topWorkflows: WorkflowCount[];
}
