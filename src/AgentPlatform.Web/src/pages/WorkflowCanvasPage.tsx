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
  Modal,
  List,
  Tag,
  Empty,
  Segmented,
} from 'antd';
import {
  SaveOutlined,
  UndoOutlined,
  RedoOutlined,
  PlayCircleOutlined,
  PlusOutlined,
  UploadOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import {
  getWorkflow,
  updateWorkflow,
  runExistingWorkflow,
  runWorkflowNode,
  runWorkflow,
  importWorkflow,
  listWorkflowApprovals,
  resolveApproval,
  getErrorMessage,
} from '../services/api';
import type { ApprovalDto, WorkflowDetail } from '../types';
import { useCanvasStore } from '../stores/workflowCanvasStore';
import DagNode from '../components/canvas/DagNode';
import NodePalette from '../components/canvas/NodePalette';
import NodeConfigPanel from '../components/canvas/NodeConfigPanel';
import VariableWatchPanel from '../components/canvas/VariableWatchPanel';
import { StepType, type ImportWorkflowRequest, type OrchestrationPresetMode } from '../types';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';

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
  http: DagNode,
  condition: DagNode,
  loop: DagNode,
  variable: DagNode,
  subworkflow: DagNode,
  delay: DagNode,
  userinput: DagNode,
};

const CanvasInner: React.FC = () => {
  const { t } = useTranslation();
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const { message } = AntApp.useApp();
  const { screenToFlowPosition } = useReactFlow();
  const userRole = useAppStore((s) => s.userRole);
  const canManage = !!userRole && (userRole.toLowerCase() === 'admin' || userRole.toLowerCase() === 'operator');

  const nodes = useCanvasStore((s) => s.nodes);
  const edges = useCanvasStore((s) => s.edges);
  const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
  const onNodesChange = useCanvasStore((s) => s.onNodesChange);
  const onEdgesChange = useCanvasStore((s) => s.onEdgesChange);
  const onConnect = useCanvasStore((s) => s.onConnect);
  const onNodeDragStart = useCanvasStore((s) => s.onNodeDragStart);
  const addNode = useCanvasStore((s) => s.addNode);
  const scaffoldAgentTeam = useCanvasStore((s) => s.scaffoldAgentTeam);
  const setSelectedNode = useCanvasStore((s) => s.setSelectedNode);
  const loadFromDetail = useCanvasStore((s) => s.loadFromDetail);
  const undo = useCanvasStore((s) => s.undo);
  const redo = useCanvasStore((s) => s.redo);
  const toPayload = useCanvasStore((s) => s.toPayload);

  const [name, setName] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [importing, setImporting] = useState(false);
  // F8 · 编排模式：默认自动（省略 preset，由后端 DetectPreset 识别含 Critic 即协商）。
  const [presetMode, setPresetMode] = useState<OrchestrationPresetMode>('auto');
  const fileRef = useRef<HTMLInputElement>(null);
  const seeded = useRef(false);

  // F20 S3 — HITL 审批门：持有完整工作流详情以检测 Paused 态，并维护审批弹窗状态。
  const [wfState, setWfState] = useState<WorkflowDetail | null>(null);
  const [approvalModalOpen, setApprovalModalOpen] = useState(false);
  const [approvals, setApprovals] = useState<ApprovalDto[]>([]);
  const [approvalLoading, setApprovalLoading] = useState(false);
  const [resolvingId, setResolvingId] = useState<string | null>(null);
  const [approvalInputs, setApprovalInputs] = useState<Record<string, string>>({});

  // 重新拉取工作流详情并同步到画布 + 本地状态（供暂停态判定/弹窗复用）。
  const refreshWorkflow = useCallback(async () => {
    if (!id) return null;
    const wf = await getWorkflow(id);
    setName(wf.name);
    loadFromDetail(wf);
    setWfState(wf);
    return wf;
  }, [id, loadFromDetail]);

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
    refreshWorkflow()
      .catch(() => message.error(t('errors.loadFailed')))
      .finally(() => setLoading(false));
  }, [id, addNode, loadFromDetail, refreshWorkflow]);

  // F20 S3 — 暂停态（currentState === 2）自动弹出 HITL 审批弹窗并加载待处理审批。
  const isPaused = Number(wfState?.currentState) === 2;

  // F8 · 协商模式指示：显式选协商，或图含 Critic 节点（后端 DetectPreset 会判 Negotiation）。
  const isNegotiationMode =
    presetMode === 'negotiation' || nodes.some((n) => n.data.stepType === StepType.Critic);

  const loadApprovals = useCallback(async () => {
    if (!id) return;
    setApprovalLoading(true);
    try {
      const list = await listWorkflowApprovals(id);
      setApprovals(list);
    } catch {
      message.error(t('canvas.hitlResolveFailed'));
    } finally {
      setApprovalLoading(false);
    }
  }, [id, message, t]);

  useEffect(() => {
    if (isPaused) {
      setApprovalModalOpen(true);
      void loadApprovals();
    } else {
      setApprovalModalOpen(false);
    }
  }, [isPaused, loadApprovals]);

  const pendingApprovals = approvals.filter((a) => a.status === 0);

  const handleResolveApproval = async (approval: ApprovalDto, approved: boolean) => {
    if (!id) return;
    setResolvingId(approval.id);
    try {
      const input = approvalInputs[approval.id] ?? '';
      const wf = await resolveApproval(id, approval.id, approved, input || null);
      message.success(t('canvas.hitlResolved'));
      setWfState(wf);
      loadFromDetail(wf);
      await loadApprovals();
    } catch (err) {
      message.error(`${t('canvas.hitlResolveFailed')}: ${getErrorMessage(err)}`);
    } finally {
      setResolvingId(null);
    }
  };

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
    } catch (err) {
      message.error(getErrorMessage(err));
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
        // F8 · 把编排模式映射为 int 预设传入（auto=省略，由后端 DetectPreset 识别）。
        await runExistingWorkflow(id, presetMode);
        message.success(t('pages.workflows.savedAndRun'));
        // refresh states（含暂停态检测与审批弹窗触发）
        await refreshWorkflow();
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
    } catch (err) {
      message.error(getErrorMessage(err));
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
      await refreshWorkflow();
      message.success(t('pages.workflows.stepDone'));
    } catch (err) {
      message.error(getErrorMessage(err));
      const wf = await getWorkflow(id).catch(() => null);
      if (wf) loadFromDetail(wf);
    } finally {
      setRunning(false);
    }
  };

  // F7 子项①：从导出 JSON 创建「新」工作流（不覆盖当前画布），跳转详情页。
  const handleImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // 允许重复选择同一文件
    if (!file) return;
    setImporting(true);
    try {
      const raw = await file.text();
      const data = JSON.parse(raw) as Partial<ImportWorkflowRequest> & { context?: string };
      const req: ImportWorkflowRequest = {
        name:
          typeof data.name === 'string' && data.name.trim()
            ? data.name.trim()
            : `Imported ${new Date().toLocaleString()}`,
        initialContext: data.initialContext ?? data.context ?? '{}',
        nodes: Array.isArray(data.nodes) ? data.nodes : null,
        edges: Array.isArray(data.edges) ? data.edges : null,
      };
      const created = await importWorkflow(req);
      message.success(t('pages.workflows.versions.imported'));
      navigate(`/workflows/${created.id}`);
    } catch (err) {
      if (err instanceof SyntaxError) message.error(t('pages.workflows.versions.importFailed'));
      else message.error(getErrorMessage(err));
    } finally {
      setImporting(false);
    }
  };

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <div style={{ height: 'calc(100vh - 120px)', display: 'flex', flexDirection: 'column' }}>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12, flexWrap: 'wrap' }} align="center">
        <Space align="center">
          <Title level={4} style={{ margin: 0 }}>
            {t('pages.workflows.canvasTitle')} {id ? t('pages.workflows.editingSuffix', { id }) : t('pages.workflows.newSuffix')}
          </Title>
          <Input
            placeholder={t('pages.workflows.namePlaceholder')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            style={{ width: 220 }}
          />
          {/* F8 · 编排模式选择器（自动 / 顺序 / 协商） */}
          <Space size={4}>
            <span style={{ color: 'rgba(0,0,0,0.45)' }}>{t('canvas.preset.label')}</span>
            <Segmented
              value={presetMode}
              onChange={(v) => setPresetMode(v as OrchestrationPresetMode)}
              options={[
                { label: t('canvas.preset.auto'), value: 'auto' },
                { label: t('canvas.preset.sequential'), value: 'sequential' },
                { label: t('canvas.preset.negotiation'), value: 'negotiation' },
              ]}
            />
          </Space>
          {isNegotiationMode && (
            <Tag color="purple" icon={<TeamOutlined />}>
              {t('canvas.negotiationMode')}
            </Tag>
          )}
        </Space>
        <Space wrap>
          <Tooltip title={t('canvas.scaffoldAgentTeamTip')}>
            <Button icon={<TeamOutlined />} onClick={scaffoldAgentTeam}>
              {t('canvas.scaffoldAgentTeam')}
            </Button>
          </Tooltip>
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
          {isPaused && (
            <Button
              type="primary"
              danger
              onClick={() => {
                setApprovalModalOpen(true);
                void loadApprovals();
              }}
            >
              {t('canvas.hitlModalTitle')}
            </Button>
          )}
          {canManage && (
            <>
              <input
                ref={fileRef}
                type="file"
                accept="application/json,.json"
                style={{ display: 'none' }}
                onChange={handleImportFile}
              />
              <Button icon={<UploadOutlined />} loading={importing} onClick={() => fileRef.current?.click()}>
                {t('pages.workflows.versions.importJson')}
              </Button>
            </>
          )}
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

      {/* F20 S3 — HITL 暂停态审批弹窗：逐条列出待处理审批门，可输入并批准/拒绝以续跑工作流。 */}
      <Modal
        open={approvalModalOpen}
        title={t('canvas.hitlModalTitle')}
        footer={null}
        onCancel={() => setApprovalModalOpen(false)}
        width={600}
        destroyOnClose
      >
        {approvalLoading ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Spin />
          </div>
        ) : pendingApprovals.length === 0 ? (
          <Empty description={t('canvas.hitlModalEmpty')} />
        ) : (
          <List
            dataSource={pendingApprovals}
            renderItem={(a) => (
              <List.Item key={a.id}>
                <div style={{ width: '100%' }}>
                  <Space direction="vertical" size="small" style={{ width: '100%' }}>
                    <Tag color="warning">{a.nodeName}</Tag>
                    <Typography.Text>{a.prompt}</Typography.Text>
                    <Input.TextArea
                      rows={3}
                      placeholder={t('canvas.hitlModalInputPlaceholder')}
                      value={approvalInputs[a.id] ?? ''}
                      onChange={(e) =>
                        setApprovalInputs((prev) => ({ ...prev, [a.id]: e.target.value }))
                      }
                    />
                    <Space>
                      <Button
                        type="primary"
                        loading={resolvingId === a.id}
                        onClick={() => handleResolveApproval(a, true)}
                      >
                        {t('canvas.hitlModalApprove')}
                      </Button>
                      <Button
                        danger
                        loading={resolvingId === a.id}
                        onClick={() => handleResolveApproval(a, false)}
                      >
                        {t('canvas.hitlModalReject')}
                      </Button>
                    </Space>
                  </Space>
                </div>
              </List.Item>
            )}
          />
        )}
      </Modal>
    </div>
  );
};

const WorkflowCanvasPage: React.FC = () => (
  <ReactFlowProvider>
    <CanvasInner />
  </ReactFlowProvider>
);

export default WorkflowCanvasPage;
