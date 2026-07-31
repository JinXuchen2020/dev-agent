import { Typography } from 'antd';
import {
  PlayCircleOutlined,
  ThunderboltOutlined,
  RobotOutlined,
  AuditOutlined,
  CheckCircleOutlined,
  BookOutlined,
  ToolOutlined,
  CodeOutlined,
  GlobalOutlined,
  BranchesOutlined,
  RetweetOutlined,
  TagsOutlined,
  ApartmentOutlined,
  ClockCircleOutlined,
  FormOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { StepType } from '../../types';
import { useTranslation } from 'react-i18next';

const PALETTE: { type: StepType; icon: ReactNode }[] = [
  { type: StepType.Start, icon: <PlayCircleOutlined /> },
  { type: StepType.LLM, icon: <ThunderboltOutlined /> },
  { type: StepType.Agent, icon: <RobotOutlined /> },
  { type: StepType.Critic, icon: <AuditOutlined /> },
  { type: StepType.Knowledge, icon: <BookOutlined /> },
  { type: StepType.Tool, icon: <ToolOutlined /> },
  { type: StepType.Code, icon: <CodeOutlined /> },
  { type: StepType.Http, icon: <GlobalOutlined /> },
  { type: StepType.Condition, icon: <BranchesOutlined /> },
  { type: StepType.Loop, icon: <RetweetOutlined /> },
  { type: StepType.Variable, icon: <TagsOutlined /> },
  { type: StepType.SubWorkflow, icon: <ApartmentOutlined /> },
  { type: StepType.Delay, icon: <ClockCircleOutlined /> },
  { type: StepType.UserInput, icon: <FormOutlined /> },
  { type: StepType.End, icon: <CheckCircleOutlined /> },
];

export default function NodePalette() {
  const { t } = useTranslation();
  const NODE_TYPE_LABEL: Record<StepType, string> = {
    [StepType.Start]: t('canvas.nodeType.start'),
    [StepType.End]: t('canvas.nodeType.end'),
    [StepType.LLM]: t('canvas.nodeType.llm'),
    [StepType.Agent]: t('canvas.nodeType.agent'),
    [StepType.Critic]: t('canvas.nodeType.critic'),
    [StepType.Knowledge]: t('canvas.nodeType.knowledge'),
    [StepType.Tool]: t('canvas.nodeType.tool'),
    [StepType.Code]: t('canvas.nodeType.code'),
    [StepType.Http]: t('canvas.nodeType.http'),
    [StepType.Condition]: t('canvas.nodeType.condition'),
    [StepType.Loop]: t('canvas.nodeType.loop'),
    [StepType.Variable]: t('canvas.nodeType.variable'),
    [StepType.SubWorkflow]: t('canvas.nodeType.subWorkflow'),
    [StepType.Delay]: t('canvas.nodeType.delay'),
    [StepType.UserInput]: t('canvas.nodeType.userInput'),
  };
  const NODE_DESC: Record<StepType, string> = {
    [StepType.Start]: t('canvas.nodeDesc.start'),
    [StepType.End]: t('canvas.nodeDesc.end'),
    [StepType.LLM]: t('canvas.nodeDesc.llm'),
    [StepType.Agent]: t('canvas.nodeDesc.agent'),
    [StepType.Critic]: t('canvas.nodeDesc.critic'),
    [StepType.Knowledge]: t('canvas.nodeDesc.knowledge'),
    [StepType.Tool]: t('canvas.nodeDesc.tool'),
    [StepType.Code]: t('canvas.nodeDesc.code'),
    [StepType.Http]: t('canvas.nodeDesc.http'),
    [StepType.Condition]: t('canvas.nodeDesc.condition'),
    [StepType.Loop]: t('canvas.nodeDesc.loop'),
    [StepType.Variable]: t('canvas.nodeDesc.variable'),
    [StepType.SubWorkflow]: t('canvas.nodeDesc.subWorkflow'),
    [StepType.Delay]: t('canvas.nodeDesc.delay'),
    [StepType.UserInput]: t('canvas.nodeDesc.userInput'),
  };
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
      <Typography.Text strong>{t('canvas.nodePanel')}</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 4 }}>
        {t('canvas.dragHint')}
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
            <div style={{ fontSize: 13, fontWeight: 600 }}>{NODE_TYPE_LABEL[p.type]}</div>
            <div style={{ fontSize: 11, color: '#8c8c8c' }}>{NODE_DESC[p.type]}</div>
          </div>
        </div>
      ))}
    </div>
  );
}
