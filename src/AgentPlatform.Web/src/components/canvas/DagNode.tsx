import { Handle, Position, type NodeProps } from '@xyflow/react';
import {
  PlayCircleOutlined,
  CheckCircleOutlined,
  ThunderboltOutlined,
  RobotOutlined,
  AuditOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { StepType } from '../../types';
import type { DagNode as DagNodeType } from '../../stores/workflowCanvasStore';

const TYPE_ICON: Record<StepType, ReactNode> = {
  [StepType.Start]: <PlayCircleOutlined />,
  [StepType.End]: <CheckCircleOutlined />,
  [StepType.LLM]: <ThunderboltOutlined />,
  [StepType.Agent]: <RobotOutlined />,
  [StepType.Critic]: <AuditOutlined />,
};

const STATE_COLOR: Record<string, string> = {
  Completed: '#52c41a',
  Running: '#1677ff',
  Failed: '#ff4d4f',
  NeedsIntervention: '#fa8c16',
  RolledBack: '#fa8c16',
  Paused: '#faad14',
};

export default function DagNode({ data, selected }: NodeProps<DagNodeType>) {
  const accent = STATE_COLOR[data.state ?? ''] ?? '#1677ff';
  const isStart = data.stepType === StepType.Start;
  const isEnd = data.stepType === StepType.End;
  const hasResult = !!data.result;

  return (
    <div
      style={{
        minWidth: 150,
        padding: '10px 12px',
        borderRadius: 8,
        border: `2px solid ${selected ? accent : '#d9d9d9'}`,
        background: '#fff',
        boxShadow: selected ? `0 0 0 3px ${accent}33` : '0 1px 3px rgba(0,0,0,0.12)',
        fontSize: 13,
      }}
    >
      {!isStart && (
        <Handle type="target" position={Position.Left} style={{ background: '#8c8c8c' }} />
      )}

      <div style={{ display: 'flex', alignItems: 'center', gap: 6, color: accent, fontWeight: 600 }}>
        {TYPE_ICON[data.stepType]}
        <span style={{ color: '#262626' }}>{data.label || '未命名'}</span>
      </div>

      {data.state && (
        <div style={{ marginTop: 4, fontSize: 11, color: accent }}>
          {data.state}
          {hasResult ? ' · 有结果' : ''}
        </div>
      )}

      {!isEnd && (
        <Handle type="source" position={Position.Right} style={{ background: '#8c8c8c' }} />
      )}
    </div>
  );
}
