import { Typography, Empty } from 'antd';
import { useCanvasStore, STEP_TYPE_LABEL } from '../../stores/workflowCanvasStore';

export default function VariableWatchPanel() {
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
      <Typography.Text strong>变量监视</Typography.Text>
      {watched.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="尚无运行结果"
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
              {n.data.label} <span style={{ color: '#8c8c8c' }}>({STEP_TYPE_LABEL[n.data.stepType]})</span>
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
