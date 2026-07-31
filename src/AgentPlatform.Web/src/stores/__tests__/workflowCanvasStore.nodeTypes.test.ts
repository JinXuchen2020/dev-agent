import { describe, it, expect, beforeEach } from 'vitest';
import {
  useCanvasStore,
  STEP_TYPE_TO_NODE_TYPE,
  NODE_TYPE_TO_STEP_TYPE,
  STEP_TYPE_LABEL,
} from '../workflowCanvasStore';
import { StepType } from '../../types';

// 验证 7 个扩展 DAG 节点类型（Http/Condition/Loop/Variable/SubWorkflow/Delay/UserInput）
// 的映射、逆映射、标签与默认配置均齐全且正确，并与后端 StepType 枚举一一对齐。
const EXTENDED_NODE_TYPES: StepType[] = [
  StepType.Http,
  StepType.Condition,
  StepType.Loop,
  StepType.Variable,
  StepType.SubWorkflow,
  StepType.Delay,
  StepType.UserInput,
];

describe('workflowCanvasStore 扩展节点类型映射', () => {
  beforeEach(() => {
    useCanvasStore.setState({ nodes: [], edges: [], selectedNodeId: null });
  });

  it('STEP_TYPE_TO_NODE_TYPE 含全部扩展类型且为预期字符串', () => {
    for (const t of EXTENDED_NODE_TYPES) {
      expect(STEP_TYPE_TO_NODE_TYPE[t]).toBeTypeOf('string');
      expect(STEP_TYPE_TO_NODE_TYPE[t]).not.toBe('');
    }
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.Http]).toBe('http');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.Condition]).toBe('condition');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.Loop]).toBe('loop');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.Variable]).toBe('variable');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.SubWorkflow]).toBe('subworkflow');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.Delay]).toBe('delay');
    expect(STEP_TYPE_TO_NODE_TYPE[StepType.UserInput]).toBe('userinput');
  });

  it('NODE_TYPE_TO_STEP_TYPE 是 STEP_TYPE_TO_NODE_TYPE 的严格逆映射', () => {
    for (const [step, nodeType] of Object.entries(STEP_TYPE_TO_NODE_TYPE)) {
      expect(NODE_TYPE_TO_STEP_TYPE[nodeType]).toBe(Number(step) as StepType);
    }
  });

  it('STEP_TYPE_LABEL 含全部扩展类型', () => {
    for (const t of EXTENDED_NODE_TYPES) {
      expect(STEP_TYPE_LABEL[t]).toBeTypeOf('string');
      expect(STEP_TYPE_LABEL[t]).not.toBe('');
    }
  });

  it('addNode 为每个扩展类型生成正确的 type / label / 默认 config', () => {
    const expectedConfig: Record<number, (c: any) => boolean> = {
      [StepType.Http]: (c: any) =>
        c.method === 'GET' && c.url === '' && c.headers === '' && c.bodyTemplate === '',
      [StepType.Condition]: (c: any) => c.expression === '',
      [StepType.Loop]: (c: any) =>
        c.itemsSource === '' && c.itemVariable === '' && Array.isArray(c.bodyNodeNames),
      [StepType.Variable]: (c: any) => c.mode === 'set' && c.name === '' && c.value === '',
      [StepType.SubWorkflow]: (c: any) => c.workflowId === '' && c.inputMapping === '',
      [StepType.Delay]: (c: any) => c.durationMs === 1000,
      [StepType.UserInput]: (c: any) => c.prompt === '' && c.approvalRole === '',
    };

    for (const t of EXTENDED_NODE_TYPES) {
      const before = useCanvasStore.getState().nodes.length;
      useCanvasStore.getState().addNode(t, { x: 0, y: 0 });
      const nodes = useCanvasStore.getState().nodes;
      expect(nodes.length).toBe(before + 1);
      const node = nodes[nodes.length - 1];
      expect(node.type).toBe(STEP_TYPE_TO_NODE_TYPE[t]);
      expect(node.data.label).toBe(STEP_TYPE_LABEL[t]);
      expect(node.data.stepType).toBe(t);
      expect(expectedConfig[t](node.data.config)).toBe(true);
    }
  });
});
