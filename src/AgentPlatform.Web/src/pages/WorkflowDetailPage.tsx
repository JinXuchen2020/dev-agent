import React, { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Spin, Descriptions, Tag, Steps, Button, Card, Space } from 'antd';
import { ArrowLeftOutlined, EditOutlined } from '@ant-design/icons';
import { getWorkflow } from '../services/api';
import type { WorkflowDetail } from '../types';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';

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
  const { t } = useTranslation();
  const userRole = useAppStore((s) => s.userRole);
  const canManage = !!userRole && (userRole.toLowerCase() === 'admin' || userRole.toLowerCase() === 'operator');
  const [wf, setWf] = useState<WorkflowDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [liveSteps, setLiveSteps] = useState<WorkflowDetail['steps'] | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Load workflow data
  useEffect(() => {
    if (!id) return;
    getWorkflow(id).then((data) => {
      setWf(data);
      setLiveSteps(data.steps);
    }).finally(() => setLoading(false));
  }, [id]);

  // Subscribe to SSE progress events via fetch (cookie auth via withCredentials).
  useEffect(() => {
    if (!id) return;
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    const processEvent = (evt: SseProgressEvent) => {
      setLiveSteps((prev) => {
        if (!prev) return prev;
        if (evt.stepName && evt.stepOrder !== null) {
          return prev.map((s) =>
            s.stepName === evt.stepName || s.order === evt.stepOrder
              ? {
                  ...s,
                  state:
                    evt.status === 'running'
                      ? 'running'
                      : evt.status === 'completed'
                        ? 'completed'
                        : evt.status === 'failed'
                          ? 'failed'
                          : s.state,
                  result: evt.result ?? s.result,
                  errorDetail: evt.errorDetail ?? s.errorDetail,
                }
              : s,
          );
        }
        if (evt.type === 'workflow_started') {
          getWorkflow(id).then(setWf);
        }
        if (evt.type === 'workflow_completed' || evt.type === 'workflow_rolledback') {
          // Reload final state after terminal event (small delay for DB persistence)
          setTimeout(() => getWorkflow(id).then(setWf), 500);
        }
        return prev;
      });
    };

    const connect = async () => {
      try {
        const res = await fetch(`/api/v1/workflows/${id}/progress`, {
          method: 'GET',
          credentials: 'include',
          signal: ctrl.signal,
        });
        if (!res.ok || !res.body) {
          // Non-2xx (e.g. 401) — do NOT loop forever like EventSource would.
          console.warn('SSE progress stream unavailable:', res.status);
          return;
        }
        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });
          let idx;
          while ((idx = buffer.indexOf('\n\n')) !== -1) {
            const frame = buffer.slice(0, idx);
            buffer = buffer.slice(idx + 2);
            const dataLine = frame.split('\n').find((l) => l.startsWith('data:'));
            if (!dataLine) continue;
            const payload = dataLine.slice(5).trim();
            if (!payload) continue;
            try {
              processEvent(JSON.parse(payload) as SseProgressEvent);
            } catch {
              // ignore keep-alive / non-JSON frames
            }
          }
        }
      } catch (err) {
        if (ctrl.signal.aborted) return;
        console.warn('SSE progress connection error', err);
      }
    };

    connect();

    return () => ctrl.abort();
  }, [id]);

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;
  if (!wf) return <Typography.Text type="danger">Workflow not found</Typography.Text>;

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/workflows')}>{t('common.back')}</Button>
        {canManage && id && (
          <Button type="primary" icon={<EditOutlined />} onClick={() => navigate(`/workflows/${id}/edit`)}>
            {t('pages.workflows.edit')}
          </Button>
        )}
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
          {(() => {
            try {
              return JSON.stringify(JSON.parse(wf.context), null, 2);
            } catch {
              return wf.context || '{}';
            }
          })()}
        </pre>
      </Card>
    </div>
  );
};

export default WorkflowDetailPage;
