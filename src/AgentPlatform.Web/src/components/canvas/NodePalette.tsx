import { Typography } from 'antd';
import {
  PlayCircleOutlined,
  ThunderboltOutlined,
  RobotOutlined,
  AuditOutlined,
  CheckCircleOutlined,
  BookOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { StepType } from '../../types';
import { STEP_TYPE_LABEL } from '../../stores/workflowCanvasStore';

const PALETTE: { type: StepType; desc: string; icon: ReactNode }[] = [
  { type: StepType.Start, desc: '工作流入口', icon: <PlayCircleOutlined /> },
  { type: StepType.LLM, desc: '一次 LLM 调用', icon: <ThunderboltOutlined /> },
  { type: StepType.Agent, desc: '分配给指定 Agent', icon: <RobotOutlined /> },
  { type: StepType.Critic, desc: '评审 / 收敛', icon: <AuditOutlined /> },
  { type: StepType.Knowledge, desc: '从知识库检索', icon: <BookOutlined /> },
  { type: StepType.End, desc: '汇总出口', icon: <CheckCircleOutlined /> },
];

export default function NodePalette() {
  const onDragStart = (e: React.DragEvent, type: StepType) => {
    e.dataTransfer.setData('application/reactflow', String(type));
    e.dataTransfer.effectAllowed = 'move';
  };

  return (
    <div
      style={{
        width: 184,
        padding: 12,
        borderRight: '1px solid #f0f0f0',
        background: '#fafafa',
        overflowY: 'auto',
      }}
    >
      <Typography.Text strong>节点面板</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 4 }}>
        拖拽到画布添加节点
      </Typography.Paragraph>
      {PALETTE.map((p) => (
        <div
          key={p.type}
          draggable
          onDragStart={(e) => onDragStart(e, p.type)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            marginBottom: 8,
            padding: '8px 10px',
            borderRadius: 6,
            border: '1px solid #d9d9d9',
            background: '#fff',
            cursor: 'grab',
            userSelect: 'none',
          }}
        >
          <span style={{ color: '#1677ff', fontSize: 16 }}>{p.icon}</span>
          <div>
            <div style={{ fontSize: 13, fontWeight: 600 }}>{STEP_TYPE_LABEL[p.type]}</div>
            <div style={{ fontSize: 11, color: '#8c8c8c' }}>{p.desc}</div>
          </div>
        </div>
      ))}
    </div>
  );
}
