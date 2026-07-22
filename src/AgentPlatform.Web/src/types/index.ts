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

export interface WorkflowDetail {
  id: string;
  name: string;
  currentState: string;
  steps: WorkflowStep[];
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
