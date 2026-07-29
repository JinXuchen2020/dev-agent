import { Typography, Empty } from 'antd';
import { useCanvasStore } from '../../stores/workflowCanvasStore';
import { StepType } from '../../types';
import { useTranslation } from 'react-i18next';

export default function VariableWatchPanel() {
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
  };
  const nodes = useCanvasStore((s) => s.nodes);
  const watched = nodes.filter((n) => n.data.state || n.data.result);

  return (
    <div
      style={{
        height: 170,
        borderTop: '1px solid #f0f0f0',
        background: '#fafafa',
        overflowY: 'auto',
        padding: 12,
      }}
    >
      <Typography.Text strong>{t('canvas.variableWatch')}</Typography.Text>
      {watched.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={t('canvas.noRunResult')}
          style={{ marginTop: 8 }}
        />
      ) : (
        watched.map((n) => (
          <div
            key={n.id}
            style={{
              marginTop: 8,
              padding: '6px 8px',
              borderRadius: 6,
              background: '#fff',
              border: '1px solid #f0f0f0',
            }}
          >
            <div style={{ fontSize: 12, fontWeight: 600 }}>
              {n.data.label} <span style={{ color: '#8c8c8c' }}>({NODE_TYPE_LABEL[n.data.stepType]})</span>
              {n.data.state ? <span style={{ marginLeft: 6, color: '#1677ff' }}>{n.data.state}</span> : null}
            </div>
            {n.data.result ? (
              <pre
                style={{
                  margin: '4px 0 0',
                  fontSize: 11,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  maxHeight: 64,
                  overflow: 'hidden',
                }}
              >
                {n.data.result}
              </pre>
            ) : null}
          </div>
        ))
      )}
    </div>
  );
}
