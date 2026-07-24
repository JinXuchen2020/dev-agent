import React, { useEffect, useState, useCallback } from 'react';
import { Table, Typography, Tag, Spin, Button, Space, Modal, Input, Select, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import type { Workflow } from '../types';
import { getWorkflows, runWorkflow, getErrorMessage } from '../services/api';
import { mapWorkflowStatus, WORKFLOW_STATUS_FILTER_OPTIONS } from '../status';

const { Title } = Typography;

const WorkflowsPage: React.FC = () => {
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
      message.warning('请输入工作流名称');
      return;
    }
    setRunning(true);
    try {
      await runWorkflow({ name: wfName.trim(), initialContext: '{}' });
      message.success('工作流已创建并运行');
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

  const columns: ColumnsType<Workflow> = [
    { title: 'Name', dataIndex: 'name', key: 'name' },
    {
      title: 'Status',
      dataIndex: 'currentState',
      key: 'currentState',
      render: (s: string | number) => {
        const m = mapWorkflowStatus(s);
        return <Tag color={m.color}>{m.label}</Tag>;
      },
    },
    { title: 'Steps', dataIndex: 'stepCount', key: 'stepCount' },
    { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (d: string) => new Date(d).toLocaleString() },
    { title: 'Updated', dataIndex: 'updatedAt', key: 'updatedAt', render: (d: string) => new Date(d).toLocaleString() },
  ];

  return (
    <div>
      <Space style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16, flexWrap: 'wrap' }}>
        <Title level={4} style={{ margin: 0 }}>
          Workflows
        </Title>
        <Space>
          <Select<number>
            allowClear
            placeholder="Filter status"
            style={{ width: 180 }}
            value={statusFilter}
            onChange={(v) => {
              setStatusFilter(v ?? undefined);
              setPage(1);
            }}
            options={WORKFLOW_STATUS_FILTER_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
          />
          <Button type="primary" onClick={() => navigate('/workflows/new')}>
            Design Workflow
          </Button>
          <Button onClick={() => setModalOpen(true)}>Quick Run</Button>
        </Space>
      </Space>
      {loading ? (
        <Spin />
      ) : (
        <Table
          columns={columns}
          dataSource={workflows}
          rowKey="id"
          pagination={{ current: page, pageSize, total, showTotal: (t) => `共 ${t} 条` }}
          onChange={(p) => {
            setPage(p.current ?? 1);
            setPageSize(p.pageSize ?? 10);
          }}
          onRow={(r) => ({ onClick: () => navigate(`/workflows/${r.id}`), style: { cursor: 'pointer' } })}
        />
      )}
      <Modal
        title="Create Workflow"
        open={modalOpen}
        confirmLoading={running}
        onOk={handleRun}
        onCancel={() => setModalOpen(false)}
        okText="Run"
      >
        <Input
          placeholder="Workflow name"
          value={wfName}
          onChange={(e) => setWfName(e.target.value)}
          onPressEnter={handleRun}
        />
      </Modal>
    </div>
  );
};

export default WorkflowsPage;
