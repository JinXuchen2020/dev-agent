export interface Agent {
  id: string;
  name: string;
  role: { roleCode: string };
  modelEndpoint?: { modelId: string };
  systemPrompt: string;
  status: string;
  createdAt: string;
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
} as const;
export type StepType = (typeof StepType)[keyof typeof StepType];

// Parsed node configuration (stored as JSON string in `configJson` on the backend).
export interface NodeConfig {
  systemPrompt?: string;
  agentId?: string | null;
  criteria?: string;
  summary?: string;
  initialContext?: string;
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
