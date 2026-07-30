import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Tag,
  Button,
  Space,
  Modal,
  Input,
  Select,
  App,
  Pagination,
  Drawer,
  List,
  Empty,
  Spin,
  Popconfirm,
} from 'antd';
import { HistoryOutlined, DownloadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { Workflow, WorkflowVersionSummary } from '../types';
import {
  getWorkflows,
  runWorkflow,
  getErrorMessage,
  getWorkflowVersions,
  createWorkflowVersion,
  restoreWorkflowVersion,
  deleteWorkflowVersion,
  exportWorkflow,
} from '../services/api';
import { useAppStore } from '../stores/appStore';
import { mapWorkflowStatus, WORKFLOW_STATUS_FILTER_OPTIONS } from '../status';
import { useTranslation } from 'react-i18next';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const { Title } = Typography;

const WorkflowsPage: React.FC = () => {
  const { t } = useTranslation();
  const { message, modal } = App.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const canManage = !!userRole && (userRole.toLowerCase() === 'admin' || userRole.toLowerCase() === 'operator');

  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<number | undefined>(undefined);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [modalOpen, setModalOpen] = useState(false);
  const [wfName, setWfName] = useState('');
  const [running, setRunning] = useState(false);
  const navigate = useNavigate();

  // ── Version history drawer state (F7 子项①) ──
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerWfId, setDrawerWfId] = useState<string | null>(null);
  const [drawerWfName, setDrawerWfName] = useState('');
  const [versions, setVersions] = useState<WorkflowVersionSummary[]>([]);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [saveNote, setSaveNote] = useState('');
  const [savingVersion, setSavingVersion] = useState(false);

  const fetch = useCallback((p: number, ps: number, status: number | undefined, signal?: AbortSignal) => {
    setLoading(true);
    getWorkflows({ status, skip: (p - 1) * ps, take: ps, signal })
      .then((d) => {
        setWorkflows(d.items);
        setTotal(d.totalCount);
      })
      .catch((err: unknown) => {
        if ((err as { name?: string })?.name !== 'CanceledError') console.error('[Workflows] fetch failed', err);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(page, pageSize, statusFilter, controller.signal);
    return () => controller.abort();
  }, [fetch, page, pageSize, statusFilter]);

  const loadVersions = useCallback((workflowId: string) => {
    setVersionsLoading(true);
    getWorkflowVersions(workflowId, { skip: 0, take: 100 })
      .then((d) => setVersions(d.items))
      .catch((err: unknown) => message.error(getErrorMessage(err)))
      .finally(() => setVersionsLoading(false));
  }, [message]);

  const handleRun = async () => {
    if (!wfName.trim()) {
      message.warning(t('pages.workflows.nameRequired'));
      return;
    }
    setRunning(true);
    try {
      await runWorkflow({ name: wfName.trim(), initialContext: '{}' });
      message.success(t('pages.workflows.created'));
      setModalOpen(false);
      setWfName('');
      setPage(1);
      const controller = new AbortController();
      fetch(page, pageSize, statusFilter, controller.signal);
    } catch (e) {
      message.error(getErrorMessage(e));
    } finally {
      setRunning(false);
    }
  };

  const openVersions = (w: Workflow) => {
    setDrawerWfId(w.id);
    setDrawerWfName(w.name);
    setSaveNote('');
    setDrawerOpen(true);
    loadVersions(w.id);
  };

  const handleSaveVersion = async () => {
    if (!drawerWfId) return;
    setSavingVersion(true);
    try {
      const v = await createWorkflowVersion(drawerWfId, saveNote);
      message.success(t('pages.workflows.versions.saved', { n: v.versionNumber }));
      setSaveNote('');
      loadVersions(drawerWfId);
    } catch (e) {
      message.error(getErrorMessage(e));
    } finally {
      setSavingVersion(false);
    }
  };

  const handleRestore = (v: WorkflowVersionSummary) => {
    if (!drawerWfId) return;
    modal.confirm({
      title: t('pages.workflows.versions.restoreConfirm'),
      onOk: async () => {
        try {
          await restoreWorkflowVersion(drawerWfId, v.id);
          message.success(t('pages.workflows.versions.restored', { n: v.versionNumber }));
          setDrawerOpen(false);
          const controller = new AbortController();
          fetch(page, pageSize, statusFilter, controller.signal);
        } catch (e) {
          message.error(getErrorMessage(e));
        }
      },
    });
  };

  const handleDeleteVersion = async (v: WorkflowVersionSummary) => {
    if (!drawerWfId) return;
    try {
      await deleteWorkflowVersion(drawerWfId, v.id);
      message.success(t('pages.workflows.versions.deleted'));
      loadVersions(drawerWfId);
    } catch (e) {
      message.error(getErrorMessage(e));
    }
  };

  const handleExport = async (w: Workflow) => {
    try {
      const exp = await exportWorkflow(w.id);
      const json = JSON.stringify(exp, null, 2);
      const blob = new Blob([json], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${(w.name || 'workflow').replace(/[\\/:*?"<>|]/g, '_')}-export.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      message.success(t('pages.workflows.versions.exported'));
    } catch (e) {
      message.error(getErrorMessage(e));
    }
  };

  const renderWorkflowCard = (w: Workflow) => {
    const status = mapWorkflowStatus(w.currentState);
    return (
      <Card title={w.name}>
        <Space direction="vertical" size={6} style={{ width: '100%' }}>
          <Tag color={status.color}>{status.label}</Tag>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.workflows.colSteps')}: {w.stepCount}
          </span>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.workflows.colCreated')}: {new Date(w.createdAt).toLocaleString()}
          </span>
          <span style={{ color: colors.textMuted, fontSize: 13 }}>
            {t('pages.workflows.colUpdated')}: {new Date(w.updatedAt).toLocaleString()}
          </span>
          <Space style={{ marginTop: 8 }} wrap>
            <Button
              size="small"
              icon={<HistoryOutlined />}
              onClick={(e) => {
                e.stopPropagation();
                openVersions(w);
              }}
            >
              {t('pages.workflows.versions.history')}
            </Button>
            <Button
              size="small"
              icon={<DownloadOutlined />}
              onClick={(e) => {
                e.stopPropagation();
                handleExport(w);
              }}
            >
              {t('pages.workflows.versions.exportJson')}
            </Button>
          </Space>
        </Space>
      </Card>
    );
  };

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16, flexWrap: 'wrap' }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.workflows.title')}
        </Title>
        <Space>
          <Select<number>
            allowClear
            placeholder={t('pages.workflows.filterStatus')}
            style={{ width: 180 }}
            value={statusFilter}
            onChange={(v) => {
              setStatusFilter(v ?? undefined);
              setPage(1);
            }}
            options={WORKFLOW_STATUS_FILTER_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
          />
          <Button type="primary" onClick={() => navigate('/workflows/new')}>
            {t('pages.workflows.newWorkflow')}
          </Button>
          <Button onClick={() => setModalOpen(true)}>{t('pages.workflows.quickRun')}</Button>
        </Space>
      </Space>
      <EntityCardGrid
        items={workflows}
        loading={loading}
        rowKey={(w) => w.id}
        emptyText={t('empty.workflows')}
        onItemClick={(w) => navigate(`/workflows/${w.id}`)}
        renderCard={renderWorkflowCard}
      />
      {!loading && total > 0 && (
        <Pagination
          style={{ marginTop: 16, textAlign: 'right' }}
          current={page}
          pageSize={pageSize}
          total={total}
          showTotal={(total) => t('common.total', { count: total })}
          onChange={(p, ps) => {
            setPage(p);
            setPageSize(ps);
          }}
        />
      )}
      <Modal
        title={t('pages.workflows.createWorkflow')}
        open={modalOpen}
        confirmLoading={running}
        onOk={handleRun}
        onCancel={() => setModalOpen(false)}
        okText={t('pages.workflows.run')}
      >
        <Input
          placeholder={t('pages.workflows.namePlaceholder')}
          value={wfName}
          onChange={(e) => setWfName(e.target.value)}
          onPressEnter={handleRun}
        />
      </Modal>

      <Drawer
        title={t('pages.workflows.versions.drawerTitle', { name: drawerWfName })}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={540}
      >
        {canManage && (
          <Space style={{ marginBottom: 16 }} wrap>
            <Input
              placeholder={t('pages.workflows.versions.saveNotePlaceholder')}
              value={saveNote}
              onChange={(e) => setSaveNote(e.target.value)}
              style={{ width: 280 }}
              onPressEnter={handleSaveVersion}
            />
            <Button type="primary" loading={savingVersion} onClick={handleSaveVersion}>
              {t('pages.workflows.versions.saveVersion')}
            </Button>
          </Space>
        )}
        {versionsLoading ? (
          <div style={{ textAlign: 'center', padding: 32 }}>
            <Spin />
          </div>
        ) : versions.length === 0 ? (
          <Empty description={t('pages.workflows.versions.listEmpty')} />
        ) : (
          <List
            dataSource={versions}
            renderItem={(v) => (
              <List.Item
                actions={
                  canManage
                    ? [
                        <Button key="restore" size="small" onClick={() => handleRestore(v)}>
                          {t('pages.workflows.versions.restore')}
                        </Button>,
                        <Popconfirm
                          key="delete"
                          title={t('pages.workflows.versions.deleteConfirm')}
                          onConfirm={() => handleDeleteVersion(v)}
                          okText={t('common.confirm')}
                          cancelText={t('common.cancel')}
                        >
                          <Button size="small" danger>
                            {t('pages.workflows.versions.deleteVersion')}
                          </Button>
                        </Popconfirm>,
                      ]
                    : []
                }
              >
                <List.Item.Meta
                  title={`v${v.versionNumber} · ${v.name}`}
                  description={
                    <Space direction="vertical" size={2}>
                      {v.note && (
                        <span>
                          {t('pages.workflows.versions.colNote')}: {v.note}
                        </span>
                      )}
                      <span>
                        {t('pages.workflows.versions.colCreatedAt')}: {new Date(v.createdAt).toLocaleString()}
                      </span>
                      {v.createdBy && (
                        <span>
                          {t('pages.workflows.versions.colCreatedBy')}: {v.createdBy}
                        </span>
                      )}
                    </Space>
                  }
                />
              </List.Item>
            )}
          />
        )}
      </Drawer>
    </div>
  );
};

export default WorkflowsPage;
