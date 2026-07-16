import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Spin, Descriptions, Tag, Steps, Button, Card, Space } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { getWorkflow } from '../services/api';
import type { WorkflowDetail } from '../types';

const { Title } = Typography;

const statusColors: Record<string, string> = {
  pending: 'default', running: 'processing', completed: 'success', failed: 'error', rolledback: 'warning',
};

const WorkflowDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [wf, setWf] = useState<WorkflowDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!id) return;
    getWorkflow(id).then(setWf).finally(() => setLoading(false));
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
        {wf.steps.length === 0 ? (
          <Typography.Text type="secondary">No steps configured. Use the workflow editor to add steps.</Typography.Text>
        ) : (
          <Steps
            direction="vertical"
            current={wf.steps.findIndex((s) => s.state === 'running' || s.state === 'pending')}
            items={wf.steps.map((s) => ({
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
