import React, { useEffect, useState, useCallback } from 'react';
import { Typography, Tag, Button, Space, Modal, Input, Select, App, Pagination } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Workflow } from '../types';
import { getWorkflows, runWorkflow, getErrorMessage } from '../services/api';
import { mapWorkflowStatus, WORKFLOW_STATUS_FILTER_OPTIONS } from '../status';
import { useTranslation } from 'react-i18next';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const { Title } = Typography;

const WorkflowsPage: React.FC = () => {
  const { t } = useTranslation();
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
  const { message } = App.useApp();

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
    </div>
  );
};

export default WorkflowsPage;
