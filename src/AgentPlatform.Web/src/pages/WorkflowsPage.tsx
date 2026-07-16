import React, { useEffect, useState } from 'react';
import { Table, Typography, Tag, Spin, Button, Space, Modal, Input } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import type { Workflow } from '../types';
import { getWorkflows, runWorkflow } from '../services/api';

const { Title } = Typography;

const statusColors: Record<string, string> = {
  pending: 'default', running: 'processing', completed: 'success', failed: 'error', rolledback: 'warning',
};

const WorkflowsPage: React.FC = () => {
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [wfName, setWfName] = useState('');
  const navigate = useNavigate();

  const fetch = () => { setLoading(true); getWorkflows().then((d) => setWorkflows(d.items)).finally(() => setLoading(false)); };
  useEffect(() => { fetch(); }, []);

  const handleRun = async () => {
    if (!wfName.trim()) return;
    await runWorkflow({ name: wfName, initialContext: '{}' });
    setModalOpen(false); setWfName(''); fetch();
  };

  const columns: ColumnsType<Workflow> = [
    { title: 'Name', dataIndex: 'name', key: 'name' },
    { title: 'Status', dataIndex: 'currentState', key: 'currentState', render: (s: string) => <Tag color={statusColors[s] || 'default'}>{s}</Tag> },
    { title: 'Steps', dataIndex: 'stepCount', key: 'stepCount' },
    { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
    { title: 'Updated', dataIndex: 'updatedAt', key: 'updatedAt', render: (d: string) => new Date(d).toLocaleString() },
  ];

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Workflows</Title>
        <Button type="primary" onClick={() => navigate('/workflows/new')}>Design Workflow</Button>
        <Button onClick={() => setModalOpen(true)}>Quick Run</Button>
      </Space>
      {loading ? <Spin /> : (
        <Table columns={columns} dataSource={workflows} rowKey="id" pagination={{ pageSize: 10 }}
          onRow={(r) => ({ onClick: () => navigate(`/workflows/${r.id}`), style: { cursor: 'pointer' } })} />
      )}
      <Modal title="Create Workflow" open={modalOpen} onOk={handleRun} onCancel={() => setModalOpen(false)}>
        <Input placeholder="Workflow name" value={wfName} onChange={(e) => setWfName(e.target.value)} />
      </Modal>
    </div>
  );
};

export default WorkflowsPage;
