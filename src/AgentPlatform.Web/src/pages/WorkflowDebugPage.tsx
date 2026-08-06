import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Typography,
  Spin,
  Card,
  Space,
  Button,
  Tag,
  Input,
  InputNumber,
  Modal,
  Descriptions,
  App as AntApp,
  List,
} from 'antd';
import { ArrowLeftOutlined, EditOutlined } from '@ant-design/icons';
import {
  getWorkflow,
  startDebugSession,
  resetDebugSession,
  debugStep,
  debugResume,
  debugRetryNode,
  debugRollback,
  getDebugState,
  getDebugVariables,
} from '../services/api';
import type { WorkflowDetail, DebugWorkflowStateSnapshot, DebugStepSnapshot } from '../types';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';

const { Title, Text } = Typography;

// 后端 WorkflowState 枚举（int）：Pending=0, Running=1, Paused=2, Completed=3, Failed=4, RolledBack=5。
const stateMeta = (t: (k: string) => string, s: number): { label: string; color: string } => {
  switch (s) {
    case 0:
      return { label: t('pages.workflows.status.pending'), color: 'default' };
    case 1:
      return { label: t('pages.workflows.status.running'), color: 'processing' };
    case 2:
      return { label: t('pages.workflows.status.paused'), color: 'warning' };
    case 3:
      return { label: t('pages.workflows.status.completed'), color: 'success' };
    case 4:
      return { label: t('pages.workflows.status.failed'), color: 'error' };
    case 5:
      return { label: t('pages.workflows.status.rolledBack'), color: 'warning' };
    default:
      return { label: t('pages.workflows.status.unknown'), color: 'default' };
  }
};

const WorkflowDebugPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { message } = AntApp.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const canManage = !!userRole && (userRole.toLowerCase() === 'admin' || userRole.toLowerCase() === 'operator');

  const [wf, setWf] = useState<WorkflowDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [variables, setVariables] = useState<Record<string, string>>({});
  const [stateSnapshot, setStateSnapshot] = useState<DebugWorkflowStateSnapshot | null>(null);
  const [lastNode, setLastNode] = useState<DebugStepSnapshot | null>(null);
  const [busy, setBusy] = useState(false);
  const [initialContext, setInitialContext] = useState('');
  const [rollbackTarget, setRollbackTarget] = useState<number>(0);
  const [retryNode, setRetryNode] = useState<DebugStepSnapshot | null>(null);
  const [overrideConfig, setOverrideConfig] = useState('');

  useEffect(() => {
    if (!id) return;
    getWorkflow(id)
      .then(setWf)
      .finally(() => setLoading(false));
  }, [id]);

  const refreshStateAndVars = async (wfId: string, sid: string) => {
    const [st, vars] = await Promise.all([getDebugState(wfId), getDebugVariables(wfId, sid)]);
    setStateSnapshot(st);
    setVariables(vars.variables ?? {});
  };

  const handleStart = async () => {
    if (!id) return;
    setBusy(true);
    try {
      const res = await startDebugSession(id, initialContext.trim() ? initialContext : undefined);
      setSessionId(res.sessionId);
      setLastNode(null);
      await refreshStateAndVars(id, res.sessionId);
      message.success(t('pages.debug.started'));
    } catch (e) {
      message.error((e as Error).message || 'start failed');
    } finally {
      setBusy(false);
    }
  };

  const handleStep = async () => {
    if (!id || !sessionId) return;
    setBusy(true);
    try {
      const res = await debugStep(id, sessionId);
      setVariables(res.variables ?? {});
      setLastNode(res.node);
      await refreshStateAndVars(id, sessionId);
      message.success(res.executed ? t('pages.debug.stepped') : t('pages.debug.notExecuted'));
    } catch (e) {
      message.error((e as Error).message || 'step failed');
    } finally {
      setBusy(false);
    }
  };

  const handleResume = async () => {
    if (!id || !sessionId) return;
    setBusy(true);
    try {
      const res = await debugResume(id, sessionId);
      setVariables(res.variables ?? {});
      setLastNode(null);
      await refreshStateAndVars(id, sessionId);
      message.success(t('pages.debug.resumed'));
    } catch (e) {
      message.error((e as Error).message || 'resume failed');
    } finally {
      setBusy(false);
    }
  };

  const handleReset = async () => {
    if (!id) return;
    setBusy(true);
    try {
      const res = await resetDebugSession(id);
      setSessionId(res.sessionId);
      setLastNode(null);
      setVariables({});
      await refreshStateAndVars(id, res.sessionId);
      message.success(t('pages.debug.resetDone'));
    } catch (e) {
      message.error((e as Error).message || 'reset failed');
    } finally {
      setBusy(false);
    }
  };

  const handleRollback = async () => {
    if (!id || !sessionId) return;
    setBusy(true);
    try {
      await debugRollback(id, sessionId, rollbackTarget);
      await refreshStateAndVars(id, sessionId);
      message.success(t('pages.debug.rollbackDone'));
    } catch (e) {
      message.error((e as Error).message || 'rollback failed');
    } finally {
      setBusy(false);
    }
  };

  const openRetry = (node: DebugStepSnapshot) => {
    setRetryNode(node);
    setOverrideConfig('');
  };

  const handleRetryConfirm = async () => {
    if (!id || !sessionId || !retryNode) return;
    setBusy(true);
    try {
      const res = await debugRetryNode(
        id,
        sessionId,
        retryNode.stepId,
        overrideConfig.trim() ? overrideConfig : undefined,
      );
      setVariables(res.variables ?? {});
      setLastNode(res.node);
      await refreshStateAndVars(id, sessionId);
      message.success(t('pages.debug.retryDone'));
      setRetryNode(null);
    } catch (e) {
      message.error((e as Error).message || 'retry failed');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <Spin style={{ display: 'block', margin: '100px auto' }} />;
  if (!wf) return <Text type="danger">Workflow not found</Text>;
  if (!canManage)
    return <Text type="warning">{t('layout.permissionHint') ?? 'Insufficient permission'}</Text>;

  const steps: DebugStepSnapshot[] =
    stateSnapshot?.steps ??
    wf.nodes.map((n) => ({
      stepId: n.id,
      order: n.order,
      stepName: n.name,
      state: 0,
      result: null,
      errorDetail: null,
    }));

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(`/workflows/${id}`)}>
          {t('common.back')}
        </Button>
        <Button type="primary" icon={<EditOutlined />} onClick={() => navigate(`/workflows/${id}/edit`)}>
          {t('pages.debug.openEditor')}
        </Button>
      </Space>

      <Card style={{ marginBottom: 16 }}>
        <Descriptions title={<Title level={4}>{wf.name} — {t('pages.debug.title')}</Title>} column={2}>
          <Descriptions.Item label={t('pages.debug.sessionLabel')}>
            {sessionId ? <Tag color="blue">{sessionId}</Tag> : <Text type="secondary">—</Text>}
          </Descriptions.Item>
          <Descriptions.Item label={t('pages.debug.stateLabel')}>
            {stateSnapshot ? (
              <Tag color={stateMeta(t, stateSnapshot.currentState).color}>
                {stateMeta(t, stateSnapshot.currentState).label}
              </Tag>
            ) : (
              <Text type="secondary">—</Text>
            )}
          </Descriptions.Item>
          <Descriptions.Item label={t('pages.debug.currentStepLabel')}>
            {stateSnapshot ? stateSnapshot.currentStepOrder : '—'}
          </Descriptions.Item>
        </Descriptions>

        <Space wrap style={{ marginTop: 8 }}>
          <Button
            type="primary"
            loading={busy}
            onClick={handleStart}
            data-testid="debug-start"
          >
            {t('pages.debug.start')}
          </Button>
          <Button loading={busy} disabled={!sessionId} onClick={handleStep} data-testid="debug-step">
            {t('pages.debug.step')}
          </Button>
          <Button loading={busy} disabled={!sessionId} onClick={handleResume} data-testid="debug-resume">
            {t('pages.debug.resume')}
          </Button>
          <Button loading={busy} disabled={!sessionId} onClick={handleReset} data-testid="debug-reset">
            {t('pages.debug.reset')}
          </Button>
          <Space.Compact>
            <InputNumber
              min={0}
              value={rollbackTarget}
              onChange={(v) => setRollbackTarget(v ?? 0)}
              disabled={!sessionId}
              style={{ width: 120 }}
              placeholder={t('pages.debug.stepOrderLabel')}
            />
            <Button
              loading={busy}
              disabled={!sessionId}
              onClick={handleRollback}
              data-testid="debug-rollback"
            >
              {t('pages.debug.rollback')}
            </Button>
          </Space.Compact>
        </Space>

        {!sessionId && (
          <div style={{ marginTop: 12 }}>
            <Text type="secondary">{t('pages.debug.enterInitialContext')}</Text>
            <Input.TextArea
              rows={2}
              value={initialContext}
              onChange={(e) => setInitialContext(e.target.value)}
              placeholder={t('pages.debug.initialContextPlaceholder')}
              style={{ marginTop: 4 }}
            />
          </div>
        )}
      </Card>

      <Card title={t('pages.debug.variablesTitle')} style={{ marginBottom: 16 }}>
        {sessionId ? (
          <pre
            data-testid="debug-variables"
            style={{ maxHeight: 300, overflow: 'auto', background: '#f5f5f5', padding: 12, borderRadius: 4 }}
          >
            {Object.keys(variables).length === 0
              ? t('pages.debug.noVariables')
              : JSON.stringify(variables, null, 2)}
          </pre>
        ) : (
          <Text type="secondary">{t('pages.debug.noSession')}</Text>
        )}
      </Card>

      {lastNode && (
        <Card title={`${t('pages.debug.lastNodeLabel')}: ${lastNode.stepName}`} style={{ marginBottom: 16 }}>
          <Descriptions column={1}>
            <Descriptions.Item label={t('pages.debug.stateLabel')}>
              <Tag color={stateMeta(t, lastNode.state).color}>
                {stateMeta(t, lastNode.state).label}
              </Tag>
            </Descriptions.Item>
            {lastNode.result && (
              <Descriptions.Item label="Result">
                <pre style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{lastNode.result}</pre>
              </Descriptions.Item>
            )}
            {lastNode.errorDetail && (
              <Descriptions.Item label={t('pages.debug.errorDetailLabel')}>
                <Text type="danger">{lastNode.errorDetail}</Text>
              </Descriptions.Item>
            )}
          </Descriptions>
        </Card>
      )}

      <Card title={t('pages.workflows.colSteps')}>
        <List
          dataSource={steps}
          renderItem={(s) => (
            <List.Item
              actions={[
                <Button
                  key="retry"
                  size="small"
                  disabled={!sessionId}
                  onClick={() => openRetry(s)}
                  data-testid={`debug-retry-${s.order}`}
                >
                  {t('pages.debug.retryNode')}
                </Button>,
              ]}
            >
              <List.Item.Meta
                title={`#${s.order} ${s.stepName}`}
                description={
                  <Space>
                    <Tag color={stateMeta(t, s.state).color}>{stateMeta(t, s.state).label}</Tag>
                    {s.result && <Text type="secondary">Result: {s.result.slice(0, 80)}</Text>}
                    {s.errorDetail && <Text type="danger">Error: {s.errorDetail.slice(0, 80)}</Text>}
                  </Space>
                }
              />
            </List.Item>
          )}
        />
      </Card>

      <Modal
        title={t('pages.debug.nodeLabel', { name: retryNode?.stepName ?? '' })}
        open={!!retryNode}
        onOk={handleRetryConfirm}
        onCancel={() => setRetryNode(null)}
        confirmLoading={busy}
        okText={t('pages.debug.retryNode')}
      >
        <Input.TextArea
          rows={4}
          value={overrideConfig}
          onChange={(e) => setOverrideConfig(e.target.value)}
          placeholder={t('pages.debug.overridePlaceholder')}
        />
      </Modal>
    </div>
  );
};

export default WorkflowDebugPage;
