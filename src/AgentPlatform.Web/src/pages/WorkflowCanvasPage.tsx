import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  useReactFlow,
  type NodeTypes,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import {
  Button,
  Typography,
  Space,
  Input,
  App as AntApp,
  Spin,
  Tooltip,
} from 'antd';
import {
  SaveOutlined,
  UndoOutlined,
  RedoOutlined,
  PlayCircleOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import {
  getWorkflow,
  updateWorkflow,
  runExistingWorkflow,
  runWorkflowNode,
  runWorkflow,
} from '../services/api';
import { useCanvasStore } from '../stores/workflowCanvasStore';
import DagNode from '../components/canvas/DagNode';
import NodePalette from '../components/canvas/NodePalette';
import NodeConfigPanel from '../components/canvas/NodeConfigPanel';
import VariableWatchPanel from '../components/canvas/VariableWatchPanel';
import { StepType } from '../types';
import { useTranslation } from 'react-i18next';

const { Title } = Typography;

const nodeTypes: NodeTypes = {
  start: DagNode,
  end: DagNode,
  llm: DagNode,
  agent: DagNode,
  critic: DagNode,
  knowledge: DagNode,
  tool: DagNode,
  code: DagNode,
};

const CanvasInner: React.FC = () => {
  const { t } = useTranslation();
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const { message } = AntApp.useApp();
  const { screenToFlowPosition } = useReactFlow();

  const nodes = useCanvasStore((s) => s.nodes);
  const edges = useCanvasStore((s) => s.edges);
  const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
  const onNodesChange = useCanvasStore((s) => s.onNodesChange);
  const onEdgesChange = useCanvasStore((s) => s.onEdgesChange);
  const onConnect = useCanvasStore((s) => s.onConnect);
  const onNodeDragStart = useCanvasStore((s) => s.onNodeDragStart);
  const addNode = useCanvasStore((s) => s.addNode);
  const setSelectedNode = useCanvasStore((s) => s.setSelectedNode);
  const loadFromDetail = useCanvasStore((s) => s.loadFromDetail);
  const undo = useCanvasStore((s) => s.undo);
  const redo = useCanvasStore((s) => s.redo);
  const toPayload = useCanvasStore((s) => s.toPayload);

  const [name, setName] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const seeded = useRef(false);

  // Load existing workflow graph.
  useEffect(() => {
    if (!id) {
      if (!seeded.current) {
        seeded.current = true;
        addNode(StepType.Start, { x: 260, y: 40 });
      }
      return;
    }
    setLoading(true);
    getWorkflow(id)
      .then((wf) => {
        setName(wf.name);
        loadFromDetail(wf);
      })
      .catch(() => message.error(t('errors.loadFailed')))
      .finally(() => setLoading(false));
  }, [id, addNode, loadFromDetail]);

  // Undo / redo hotkeys (ignore while typing in fields).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault();
        if (e.shiftKey) redo();
        else undo();
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
        e.preventDefault();
        redo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [undo, redo]);

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
  }, []);

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      const raw = e.dataTransfer.getData('application/reactflow');
      if (!raw) return;
      const stepType = Number(raw) as StepType;
      if (Number.isNaN(stepType)) return;
      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY });
      addNode(stepType, position);
    },
    [screenToFlowPosition, addNode],
  );

  const hasStartEnd = () => {
    const types = nodes.map((n) => n.data.stepType);
    return types.includes(StepType.Start) && types.includes(StepType.End);
  };

  const buildStepsForNew = () =>
    [...nodes]
      .sort((a, b) => a.position.y - b.position.y || a.position.x - b.position.x)
      .map((n) => n.data.label);

  const handleSaveDraft = async () => {
    if (!id) {
      message.error(t('pages.workflows.draftEditOnly'));
      return;
    }
    if (!name.trim()) return message.error(t('pages.workflows.nameRequired'));
    if (!hasStartEnd()) return message.error(t('pages.workflows.dagStartEnd'));
    setSaving(true);
    try {
      const payload = toPayload();
      await updateWorkflow(id, {
        name: name.trim(),
        initialContext: payload.initialContext,
        nodes: payload.nodes,
        edges: payload.edges,
      });
      message.success(t('pages.workflows.draftSaved'));
      navigate('/workflows');
    } catch {
      message.error(t('pages.workflows.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleSaveAndRun = async () => {
    if (!name.trim()) return message.error(t('pages.workflows.nameRequired'));
    if (!hasStartEnd()) return message.error(t('pages.workflows.dagStartEnd'));
    setSaving(true);
    try {
      const payload = toPayload();
      if (id) {
        await updateWorkflow(id, {
          name: name.trim(),
          initialContext: payload.initialContext,
          nodes: payload.nodes,
          edges: payload.edges,
        });
        await runExistingWorkflow(id);
        message.success(t('pages.workflows.savedAndRun'));
        // refresh states
        const wf = await getWorkflow(id);
        loadFromDetail(wf);
      } else {
        // New workflow: create via linear steps, then persist the graph, then open editor.
        const created = await runWorkflow({
          name: name.trim(),
          initialContext: payload.initialContext,
          steps: buildStepsForNew(),
        });
        await updateWorkflow(created.id, {
          name: name.trim(),
          initialContext: payload.initialContext,
          nodes: payload.nodes,
          edges: payload.edges,
        });
        message.success(t('pages.workflows.createdDag'));
        navigate(`/workflows/${created.id}/edit`);
      }
    } catch {
      message.error(t('pages.workflows.saveRunFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleRunNode = async () => {
    if (!id) return message.error(t('pages.workflows.saveFirstDebug'));
    if (!selectedNodeId) return message.error(t('pages.workflows.selectNode'));
    setRunning(true);
    try {
      await runWorkflowNode(id, selectedNodeId);
      const wf = await getWorkflow(id);
      loadFromDetail(wf);
      message.success(t('pages.workflows.stepDone'));
    } catch {
      message.error(t('pages.workflows.stepFailed'));
      const wf = await getWorkflow(id).catch(() => null);
      if (wf) loadFromDetail(wf);
    } finally {
      setRunning(false);
    }
  };

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <div style={{ height: 'calc(100vh - 120px)', display: 'flex', flexDirection: 'column' }}>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12, flexWrap: 'wrap' }}>
        <Space>
          <Title level={4} style={{ margin: 0 }}>
            {t('pages.workflows.canvasTitle')} {id ? t('pages.workflows.editingSuffix', { id }) : t('pages.workflows.newSuffix')}
          </Title>
          <Input
            placeholder={t('pages.workflows.namePlaceholder')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            style={{ width: 220 }}
          />
        </Space>
        <Space wrap>
          <Tooltip title={t('pages.workflows.undoTip')}>
            <Button icon={<UndoOutlined />} onClick={undo}>
              {t('pages.workflows.undo')}
            </Button>
          </Tooltip>
          <Tooltip title={t('pages.workflows.redoTip')}>
            <Button icon={<RedoOutlined />} onClick={redo}>
              {t('pages.workflows.redo')}
            </Button>
          </Tooltip>
          <Tooltip title={t('pages.workflows.addNodeTip')}>
            <Button icon={<PlusOutlined />} onClick={() => addNode(StepType.LLM, { x: 320, y: 200 })}>
              {t('pages.workflows.addNode')}
            </Button>
          </Tooltip>
          <Button
            icon={<PlayCircleOutlined />}
            onClick={handleRunNode}
            loading={running}
            disabled={!id || !selectedNodeId}
          >
            {t('pages.workflows.stepRun')}
          </Button>
          <Button onClick={handleSaveDraft} disabled={!id || saving}>
            {t('pages.workflows.saveDraft')}
          </Button>
          <Button type="primary" icon={<SaveOutlined />} onClick={handleSaveAndRun} loading={saving}>
            {t('pages.workflows.saveAndRun')}
          </Button>
        </Space>
      </Space>

      <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
        <NodePalette />
        <div style={{ flex: 1, position: 'relative' }} onDrop={onDrop} onDragOver={onDragOver}>
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeDragStart={onNodeDragStart}
            onNodeClick={(_, n) => setSelectedNode(n.id)}
            onPaneClick={() => setSelectedNode(null)}
            fitView
            deleteKeyCode={['Backspace', 'Delete']}
          >
            <Background variant={BackgroundVariant.Dots} gap={16} />
            <Controls />
            <MiniMap pannable zoomable />
          </ReactFlow>
        </div>
        <NodeConfigPanel />
      </div>

      <VariableWatchPanel />
    </div>
  );
};

const WorkflowCanvasPage: React.FC = () => (
  <ReactFlowProvider>
    <CanvasInner />
  </ReactFlowProvider>
);

export default WorkflowCanvasPage;
