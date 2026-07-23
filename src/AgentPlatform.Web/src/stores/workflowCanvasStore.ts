import { create } from 'zustand';
import {
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  type Node,
  type Edge,
  type NodeChange,
  type EdgeChange,
  type Connection,
} from '@xyflow/react';
import { StepType } from '../types';
import type {
  NodeConfig,
  WorkflowDetail,
  WorkflowNodeRequest,
  WorkflowEdgeRequest,
} from '../types';

export type DagNodeData = {
  label: string;
  stepType: StepType;
  state?: string;
  result?: string | null;
  errorDetail?: string | null;
  config?: NodeConfig;
  assignedAgentId?: string | null;
  [key: string]: unknown;
};

export type DagNode = Node<DagNodeData>;
export type DagEdge = Edge;

interface Snapshot {
  nodes: DagNode[];
  edges: DagEdge[];
}

export const STEP_TYPE_TO_NODE_TYPE: Record<StepType, string> = {
  [StepType.Start]: 'start',
  [StepType.End]: 'end',
  [StepType.LLM]: 'llm',
  [StepType.Agent]: 'agent',
  [StepType.Critic]: 'critic',
};

export const NODE_TYPE_TO_STEP_TYPE: Record<string, StepType> = {
  start: StepType.Start,
  end: StepType.End,
  llm: StepType.LLM,
  agent: StepType.Agent,
  critic: StepType.Critic,
};

export const STEP_TYPE_LABEL: Record<StepType, string> = {
  [StepType.Start]: 'Start',
  [StepType.End]: 'End',
  [StepType.LLM]: 'LLM',
  [StepType.Agent]: 'Agent',
  [StepType.Critic]: 'Critic',
};

function newId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `n-${Date.now()}-${Math.floor(Math.random() * 1e6)}`;
}

function defaultConfig(stepType: StepType): NodeConfig {
  switch (stepType) {
    case StepType.LLM:
      return { systemPrompt: '' };
    case StepType.Agent:
      return { agentId: null };
    case StepType.Critic:
      return { criteria: '' };
    case StepType.End:
      return { summary: 'all' };
    default:
      return {};
  }
}

function parseConfig(json?: string | null): NodeConfig {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json);
    return typeof parsed === 'object' && parsed !== null ? (parsed as NodeConfig) : {};
  } catch {
    return {};
  }
}

interface CanvasState {
  nodes: DagNode[];
  edges: DagEdge[];
  selectedNodeId: string | null;
  initialContext: string;
  past: Snapshot[];
  future: Snapshot[];

  onNodesChange: (changes: NodeChange<DagNode>[]) => void;
  onEdgesChange: (changes: EdgeChange<DagEdge>[]) => void;
  onConnect: (connection: Connection) => void;
  onNodeDragStart: () => void;

  addNode: (stepType: StepType, position: { x: number; y: number }) => void;
  removeNode: (id: string) => void;
  removeEdge: (id: string) => void;
  snapshot: () => void;
  setNodeData: (id: string, patch: Partial<DagNodeData>, commit?: boolean) => void;
  setSelectedNode: (id: string | null) => void;
  setInitialContext: (ctx: string) => void;

  hydrate: (payload: { nodes: DagNode[]; edges: DagEdge[]; initialContext: string }) => void;
  loadFromDetail: (detail: WorkflowDetail) => void;
  undo: () => void;
  redo: () => void;
  toPayload: () => {
    nodes: WorkflowNodeRequest[];
    edges: WorkflowEdgeRequest[];
    initialContext: string;
  };
}

export const useCanvasStore = create<CanvasState>((set, get) => {
  const pushHistory = () => {
    const { nodes, edges, past } = get();
    set({ past: [...past, { nodes, edges }].slice(-50), future: [] });
  };

  return {
    nodes: [],
    edges: [],
    selectedNodeId: null,
    initialContext: '',
    past: [],
    future: [],

    onNodesChange: (changes) => {
      if (changes.some((c) => c.type === 'remove' || c.type === 'add')) pushHistory();
      set({ nodes: applyNodeChanges(changes, get().nodes) });
    },

    onEdgesChange: (changes) => {
      if (changes.some((c) => c.type === 'remove' || c.type === 'add')) pushHistory();
      set({ edges: applyEdgeChanges(changes, get().edges) });
    },

    onConnect: (connection) => {
      pushHistory();
      set({ edges: addEdge({ ...connection, id: newId() }, get().edges) });
    },

    onNodeDragStart: () => pushHistory(),

    addNode: (stepType, position) => {
      pushHistory();
      const id = newId();
      const node: DagNode = {
        id,
        type: STEP_TYPE_TO_NODE_TYPE[stepType],
        position,
        data: {
          label: STEP_TYPE_LABEL[stepType],
          stepType,
          config: defaultConfig(stepType),
        },
      };
      set({ nodes: [...get().nodes, node], selectedNodeId: id });
    },

    removeNode: (id) => {
      pushHistory();
      set({
        nodes: get().nodes.filter((n) => n.id !== id),
        edges: get().edges.filter((e) => e.source !== id && e.target !== id),
        selectedNodeId: get().selectedNodeId === id ? null : get().selectedNodeId,
      });
    },

    removeEdge: (id) => {
      pushHistory();
      set({ edges: get().edges.filter((e) => e.id !== id) });
    },

    snapshot: () => pushHistory(),

    setNodeData: (id, patch, commit) => {
      if (commit) pushHistory();
      set({
        nodes: get().nodes.map((n) =>
          n.id === id ? { ...n, data: { ...n.data, ...patch } } : n,
        ),
      });
    },

    setSelectedNode: (id) => set({ selectedNodeId: id }),

    setInitialContext: (ctx) => set({ initialContext: ctx }),

    hydrate: ({ nodes, edges, initialContext }) => {
      set({ nodes, edges, initialContext, selectedNodeId: null, past: [], future: [] });
    },

    loadFromDetail: (detail) => {
      let nodes: DagNode[];
      let edges: DagEdge[];

      if (detail.nodes && detail.nodes.length > 0) {
        nodes = detail.nodes.map((n) => ({
          id: n.id,
          type: STEP_TYPE_TO_NODE_TYPE[n.type],
          position: { x: n.positionX, y: n.positionY },
          data: {
            label: n.name,
            stepType: n.type,
            state: n.state,
            result: n.result,
            errorDetail: n.errorDetail,
            config: parseConfig(n.configJson),
            assignedAgentId: n.assignedAgentId,
          },
        }));
        edges = (detail.edges ?? []).map((e) => ({
          id: e.id,
          source: e.sourceNodeId,
          target: e.targetNodeId,
          label: e.label ?? undefined,
        }));
      } else if (detail.steps && detail.steps.length > 0) {
        // Legacy fallback: chain the linear steps into nodes.
        nodes = detail.steps.map((s, i) => ({
          id: s.id,
          type: 'llm',
          position: { x: 200 + (i % 3) * 160, y: 120 + Math.floor(i / 3) * 140 },
          data: {
            label: s.stepName,
            stepType: StepType.LLM,
            state: s.state,
            result: s.result,
            errorDetail: s.errorDetail,
            config: {},
            assignedAgentId: s.assignedAgentId,
          },
        }));
        edges = detail.steps.slice(1).map((s, i) => ({
          id: `e-${detail.steps[i].id}-${s.id}`,
          source: detail.steps[i].id,
          target: s.id,
        }));
      } else {
        nodes = [];
        edges = [];
      }

      set({
        nodes,
        edges,
        initialContext: detail.context ?? '',
        selectedNodeId: null,
        past: [],
        future: [],
      });
    },

    undo: () => {
      const { past, future, nodes, edges } = get();
      if (past.length === 0) return;
      const previous = past[past.length - 1];
      set({
        nodes: previous.nodes,
        edges: previous.edges,
        past: past.slice(0, -1),
        future: [{ nodes, edges }, ...future].slice(0, 50),
      });
    },

    redo: () => {
      const { past, future, nodes, edges } = get();
      if (future.length === 0) return;
      const next = future[0];
      set({
        nodes: next.nodes,
        edges: next.edges,
        past: [...past, { nodes, edges }].slice(-50),
        future: future.slice(1),
      });
    },

    toPayload: () => {
      const { nodes, edges, initialContext } = get();
      return {
        nodes: nodes.map((n) => ({
          id: n.id,
          type: n.data.stepType,
          name: n.data.label,
          position: { x: n.position.x, y: n.position.y },
          config: n.data.config ? JSON.stringify(n.data.config) : null,
          assignedAgentId: n.data.assignedAgentId ?? null,
        })),
        edges: edges.map((e) => ({
          id: e.id,
          source: e.source,
          target: e.target,
          label: typeof e.label === 'string' ? e.label : null,
        })),
        initialContext,
      };
    },
  };
});
