import React, { useCallback, useState, useRef } from 'react';
import {
  ReactFlow,
  addEdge,
  applyNodeChanges,
  applyEdgeChanges,
  type Node,
  type Edge,
  type NodeChange,
  type EdgeChange,
  type Connection,
  Background,
  Controls,
  MiniMap,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { Button, Typography, Card, Space, Modal, Input, message } from 'antd';
import { PlusOutlined, SaveOutlined } from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import { runWorkflow } from '../services/api';

const { Title } = Typography;

const initialNodes: Node[] = [
  { id: 'start', type: 'input', position: { x: 250, y: 0 }, data: { label: 'Start' } },
];

const WorkflowEditorPage: React.FC = () => {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const [nodes, setNodes] = useState<Node[]>(initialNodes);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [name, setName] = useState('');
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const nodeIdCounter = useRef(1);

  const onNodesChange = useCallback((changes: NodeChange[]) => {
    setNodes((nds) => applyNodeChanges(changes, nds));
  }, []);

  const onEdgesChange = useCallback((changes: EdgeChange[]) => {
    setEdges((eds) => applyEdgeChanges(changes, eds));
  }, []);

  const onConnect = useCallback((connection: Connection) => {
    setEdges((eds) => addEdge(connection, eds));
  }, []);

  const addStepNode = useCallback(() => {
    const idx = nodeIdCounter.current++;
    const newNodeId = `step-${idx}`;
    const newNode: Node = {
      id: newNodeId,
      type: 'default',
      position: { x: 200 + Math.random() * 100, y: nodes.length * 100 },
      data: { label: `Step ${idx}` },
    };
    setNodes((nds) => [...nds, newNode]);
  }, [nodes.length]);

  const handleSave = async () => {
    if (!name.trim()) {
      message.error('Please enter a workflow name');
      return;
    }
    setSaving(true);
    try {
      const stepNames = nodes
        .filter((n) => n.id.startsWith('step-'))
        .map((n) => n.data.label as string);
      const initialContext = JSON.stringify({ steps: stepNames, edges: edges.map((e) => ({ from: e.source, to: e.target })) });
      await runWorkflow({ name, initialContext });
      message.success('Workflow created successfully');
      setSaveModalOpen(false);
      navigate('/workflows');
    } catch {
      message.error('Failed to create workflow');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div style={{ height: 'calc(100vh - 120px)', display: 'flex', flexDirection: 'column' }}>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Workflow Editor {id ? `(Edit: ${id})` : ''}</Title>
        <Space>
          <Button icon={<PlusOutlined />} onClick={addStepNode}>Add Step</Button>
          <Button type="primary" icon={<SaveOutlined />} onClick={() => setSaveModalOpen(true)}>Save & Run</Button>
        </Space>
      </Space>
      <Card style={{ flex: 1, padding: 0 }} bodyStyle={{ height: '100%', padding: 0 }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          fitView
        >
          <Background />
          <Controls />
          <MiniMap />
        </ReactFlow>
      </Card>

      <Modal title="Save Workflow" open={saveModalOpen} onOk={handleSave} onCancel={() => setSaveModalOpen(false)} confirmLoading={saving}>
        <Input placeholder="Workflow name" value={name} onChange={(e) => setName(e.target.value)} />
      </Modal>
    </div>
  );
};

export default WorkflowEditorPage;
