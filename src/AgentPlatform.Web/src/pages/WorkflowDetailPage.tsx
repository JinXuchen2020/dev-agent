import React, { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Spin, Descriptions, Tag, Steps, Button, Card, Space, message } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { getWorkflow } from '../services/api';
import type { WorkflowDetail } from '../types';

const { Title } = Typography;

const statusColors: Record<string, string> = {
  pending: 'default', running: 'processing', completed: 'success', failed: 'error', rolledback: 'warning',
};

interface SseProgressEvent {
  type: string;
  workflowId: string;
  executionLogId: string | null;
  stepName: string | null;
  stepOrder: number | null;
  status: string;
  result: string | null;
  errorDetail: string | null;
  timestamp: string;
}

const WorkflowDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [wf, setWf] = useState<WorkflowDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [liveSteps, setLiveSteps] = useState<WorkflowDetail['steps'] | null>(null);
  const eventSourceRef = useRef<EventSource | null>(null);

  // Load workflow data
  useEffect(() => {
    if (!id) return;
    getWorkflow(id).then((data) => {
      setWf(data);
      setLiveSteps(data.steps);
    }).finally(() => setLoading(false));
  }, [id]);

  // Subscribe to SSE progress events for real-time updates
  useEffect(() => {
    if (!id) return;

    const es = new EventSource(`/api/v1/workflows/${id}/progress`);
    eventSourceRef.current = es;

    es.onmessage = (event) => {
      try {
        const evt: SseProgressEvent = JSON.parse(event.data);
        // Update live step states based on incoming progress events
        setLiveSteps((prev) => {
          if (!prev) return prev;
          if (evt.stepName && evt.stepOrder !== null) {
            return prev.map((s) =>
              s.stepName === evt.stepName || s.order === evt.stepOrder
                ? { ...s, state: evt.status === 'running' ? 'running' : evt.status === 'completed' ? 'completed' : evt.status === 'failed' ? 'failed' : s.state, result: evt.result ?? s.result, errorDetail: evt.errorDetail ?? s.errorDetail }
                : s,
            );
          }
          if (evt.type === 'workflow_started') {
            // Reload to get the execution log ID associated
            getWorkflow(id).then(setWf);
          }
          if (evt.type === 'workflow_completed' || evt.type === 'workflow_rolledback') {
            // Reload final state after terminal event (small delay for DB persistence)
            setTimeout(() => getWorkflow(id).then(setWf), 500);
            es.close();
          }
          return prev;
        });
      } catch {
        // Ignore parse errors from keep-alive or non-JSON events
      }
    };

    es.onerror = () => {
      // EventSource auto-reconnects by default; log only on first error
      console.warn('SSE connection error for workflow', id);
    };

    return () => {
      es.close();
    };
  }, [id]);

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;
  if (!wf) return <Typography.Text type="danger">Workflow not found</Typography.Text>;

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/workflows')}>Back</Button>
      </Space>
      <Card>
        <Descriptions title={<Title level={4}>{wf.name}</Title>} column={2}>
          <Descriptions.Item label="Status"><Tag color={statusColors[wf.currentState]}>{wf.currentState}</Tag></Descriptions.Item>
          <Descriptions.Item label="Steps">{wf.steps.length}</Descriptions.Item>
          <Descriptions.Item label="Created">{new Date(wf.createdAt).toLocaleString()}</Descriptions.Item>
          <Descriptions.Item label="Updated">{new Date(wf.updatedAt).toLocaleString()}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="Workflow Steps" style={{ marginTop: 16 }}>
        {(liveSteps ?? wf.steps).length === 0 ? (
          <Typography.Text type="secondary">No steps configured. Use the workflow editor to add steps.</Typography.Text>
        ) : (
          <Steps
            direction="vertical"
            current={(liveSteps ?? wf.steps).findIndex((s) => s.state === 'running' || s.state === 'pending')}
            items={(liveSteps ?? wf.steps).map((s) => ({
              title: s.stepName,
              description: `Order: ${s.order} | State: ${s.state}${s.assignedAgentId ? ` | Agent: ${s.assignedAgentId}` : ''}${s.result ? ` | Result: ${s.result}` : ''}${s.errorDetail ? ` | Error: ${s.errorDetail}` : ''}`,
              status: s.state === 'completed' ? 'finish' : s.state === 'failed' ? 'error' : s.state === 'running' ? 'process' : 'wait',
            }))}
          />
        )}
      </Card>

      <Card title="Shared Context" style={{ marginTop: 16 }}>
        <pre style={{ maxHeight: 300, overflow: 'auto', background: '#f5f5f5', padding: 12, borderRadius: 4 }}>
          {JSON.stringify(JSON.parse(wf.context), null, 2)}
        </pre>
      </Card>
    </div>
  );
};

export default WorkflowDetailPage;
