import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Table,
  Button,
  Modal,
  Form,
  Input,
  Select,
  Space,
  Tag,
  Drawer,
  Descriptions,
  Progress,
  App as AntApp,
  Popconfirm,
  Card,
} from 'antd';
import {
  PlusOutlined,
  DeleteOutlined,
  EditOutlined,
  PlayCircleOutlined,
  EyeOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import {
  getEvaluationDatasets,
  getEvaluationDataset,
  createEvaluationDataset,
  updateEvaluationDataset,
  deleteEvaluationDataset,
  runEvaluation,
  getWorkflows,
} from '../services/api';
import type {
  EvaluationDatasetSummary,
  EvaluationDatasetDetail,
  EvaluationReport,
  EvaluationMatchMode,
  Workflow,
} from '../types';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';

const { Title, Text } = Typography;
const { TextArea } = Input;

interface CaseFormItem {
  input: string;
  expectedOutput: string;
  matchMode: EvaluationMatchMode;
}

const EvaluationDatasetsPage: React.FC = () => {
  const { t } = useTranslation();
  const { message } = AntApp.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const canWrite =
    !!userRole &&
    (userRole.toLowerCase() === 'admin' || userRole.toLowerCase() === 'operator');

  const [datasets, setDatasets] = useState<EvaluationDatasetSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState('');

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<EvaluationDatasetDetail | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm();

  const [runModalOpen, setRunModalOpen] = useState(false);
  const [runTarget, setRunTarget] = useState<EvaluationDatasetSummary | null>(null);
  const [workflowOptions, setWorkflowOptions] = useState<{ label: string; value: string }[]>([]);
  const [selectedWorkflow, setSelectedWorkflow] = useState<string | undefined>();
  const [running, setRunning] = useState(false);

  const [report, setReport] = useState<EvaluationReport | null>(null);
  const [reportOpen, setReportOpen] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    getEvaluationDatasets(keyword || undefined)
      .then(setDatasets)
      .catch(() => message.error(t('pages.evaluation.loadFailed')))
      .finally(() => setLoading(false));
  }, [keyword, message, t]);

  useEffect(() => {
    load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ name: '', description: '', cases: [{ input: '', expectedOutput: '', matchMode: 0 }] });
    setModalOpen(true);
  };

  const openEdit = (ds: EvaluationDatasetSummary) => {
    setSaving(true);
    getEvaluationDataset(ds.id)
      .then((detail) => {
        setEditing(detail);
        form.setFieldsValue({
          name: detail.name,
          description: detail.description ?? '',
          cases: detail.cases.map((c) => ({
            input: c.input,
            expectedOutput: c.expectedOutput,
            matchMode: c.matchMode,
          })),
        });
        setModalOpen(true);
      })
      .catch(() => message.error(t('pages.evaluation.loadFailed')))
      .finally(() => setSaving(false));
  };

  const handleSave = () => {
    form.validateFields().then((values: { name: string; description?: string; cases: CaseFormItem[] }) => {
      setSaving(true);
      const payload = {
        name: values.name,
        description: values.description || null,
        cases: values.cases.map((c) => ({
          input: c.input,
          expectedOutput: c.expectedOutput,
          matchMode: c.matchMode,
        })),
      };
      const op = editing
        ? updateEvaluationDataset(editing.id, payload)
        : createEvaluationDataset(payload);
      op
        .then(() => {
          message.success(editing ? t('pages.evaluation.updated') : t('pages.evaluation.created'));
          setModalOpen(false);
          load();
        })
        .catch(() => message.error(editing ? t('pages.evaluation.updateFailed') : t('pages.evaluation.createFailed')))
        .finally(() => setSaving(false));
    });
  };

  const handleDelete = (id: string) => {
    deleteEvaluationDataset(id)
      .then(() => {
        message.success(t('pages.evaluation.deleted'));
        load();
      })
      .catch(() => message.error(t('pages.evaluation.deleteFailed')));
  };

  const openRun = (ds: EvaluationDatasetSummary) => {
    setRunTarget(ds);
    setSelectedWorkflow(undefined);
    setRunning(false);
    getWorkflows({ take: 200 })
      .then((r) =>
        setWorkflowOptions(
          (r.items as Workflow[]).map((w) => ({ label: w.name, value: w.id })),
        ),
      )
      .catch(() => setWorkflowOptions([]));
    setRunModalOpen(true);
  };

  const handleRun = () => {
    if (!runTarget || !selectedWorkflow) {
      message.warning(t('pages.evaluation.runWorkflowPlaceholder'));
      return;
    }
    setRunning(true);
    runEvaluation(runTarget.id, selectedWorkflow)
      .then((rep) => {
        setReport(rep);
        setRunModalOpen(false);
        setReportOpen(true);
      })
      .catch(() => message.error(t('pages.evaluation.runFailed')))
      .finally(() => setRunning(false));
  };

  const columns: ColumnsType<EvaluationDatasetSummary> = [
    { title: t('pages.evaluation.title'), dataIndex: 'name', key: 'name' },
    {
      title: t('pages.evaluation.descriptionLabel'),
      dataIndex: 'description',
      key: 'description',
      render: (d: string | null) => d || '-',
    },
    { title: t('pages.evaluation.caseCount'), dataIndex: 'caseCount', key: 'caseCount', width: 90 },
    {
      title: t('pages.evaluation.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 180,
      render: (d: string) => new Date(d).toLocaleString(),
    },
    {
      title: t('common.actions'),
      key: 'actions',
      width: 200,
      render: (_: unknown, row: EvaluationDatasetSummary) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} onClick={() => openEdit(row)}>
            {t('common.view')}
          </Button>
          {canWrite && (
            <>
              <Button size="small" icon={<PlayCircleOutlined />} onClick={() => openRun(row)}>
                {t('pages.evaluation.run')}
              </Button>
              <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)}>
                {t('common.edit')}
              </Button>
              <Popconfirm
                title={t('pages.evaluation.confirmDelete')}
                onConfirm={() => handleDelete(row.id)}
                okText={t('common.confirm')}
                cancelText={t('common.cancel')}
              >
                <Button size="small" danger icon={<DeleteOutlined />} />
              </Popconfirm>
            </>
          )}
        </Space>
      ),
    },
  ];

  const reportColumns: ColumnsType<EvaluationReport['cases'][number]> = [
    { title: t('pages.evaluation.colInput'), dataIndex: 'input', key: 'input', ellipsis: true },
    { title: t('pages.evaluation.colExpected'), dataIndex: 'expectedOutput', key: 'expectedOutput', ellipsis: true },
    {
      title: t('pages.evaluation.colActual'),
      dataIndex: 'actualOutput',
      key: 'actualOutput',
      ellipsis: true,
      render: (v: string | null) => v || '-',
    },
    {
      title: t('pages.evaluation.colPassed'),
      dataIndex: 'passed',
      key: 'passed',
      width: 90,
      render: (p: boolean) =>
        p ? <Tag color="success">{t('pages.evaluation.passed')}</Tag> : <Tag color="error">{t('pages.evaluation.failed')}</Tag>,
    },
    {
      title: t('pages.evaluation.colTokens'),
      key: 'tokens',
      width: 120,
      render: (_: unknown, r) => `${r.tokensIn}/${r.tokensOut}`,
    },
    { title: t('pages.evaluation.colDuration'), dataIndex: 'durationMs', key: 'durationMs', width: 110 },
    {
      title: t('pages.evaluation.colError'),
      dataIndex: 'errorDetail',
      key: 'errorDetail',
      ellipsis: true,
      render: (e: string | null) => (e ? <Text type="danger">{e}</Text> : '-'),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16, justifyContent: 'space-between', width: '100%' }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.evaluation.title')}
        </Title>
        <Space>
          <Input.Search
            placeholder={t('pages.evaluation.searchPlaceholder')}
            allowClear
            onSearch={(v) => setKeyword(v)}
            style={{ width: 240 }}
          />
          {canWrite && (
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              {t('pages.evaluation.newDataset')}
            </Button>
          )}
        </Space>
      </Space>
      <Text type="secondary" style={{ display: 'block', marginBottom: 16 }}>
        {t('pages.evaluation.subtitle')}
      </Text>

      <Card>
        <Table
          rowKey="id"
          loading={loading}
          columns={columns}
          dataSource={datasets}
          pagination={{ pageSize: 10 }}
          locale={{ emptyText: t('pages.evaluation.empty') }}
        />
      </Card>

      <Modal
        title={editing ? t('pages.evaluation.editDataset') : t('pages.evaluation.newDataset')}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSave}
        confirmLoading={saving}
        width={720}
        okText={t('pages.evaluation.save')}
        cancelText={t('pages.evaluation.cancel')}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('pages.evaluation.nameLabel')} rules={[{ required: true, message: t('pages.evaluation.namePlaceholder') }]}>
            <Input placeholder={t('pages.evaluation.namePlaceholder')} />
          </Form.Item>
          <Form.Item name="description" label={t('pages.evaluation.descriptionLabel')}>
            <Input.TextArea rows={2} placeholder={t('pages.evaluation.descriptionPlaceholder')} />
          </Form.Item>
          <Form.List name="cases">
            {(fields, { add, remove }) => (
              <div>
                <Text strong>{t('pages.evaluation.casesLabel')}</Text>
                {fields.map((field) => (
                  <Space key={field.key} align="start" style={{ display: 'flex', marginBottom: 8 }}>
                    <Form.Item {...field} name={[field.name, 'input']} rules={[{ required: true }]} style={{ marginBottom: 0, width: 220 }}>
                      <TextArea rows={2} placeholder={t('pages.evaluation.caseInput')} />
                    </Form.Item>
                    <Form.Item {...field} name={[field.name, 'expectedOutput']} rules={[{ required: true }]} style={{ marginBottom: 0, width: 220 }}>
                      <TextArea rows={2} placeholder={t('pages.evaluation.caseExpected')} />
                    </Form.Item>
                    <Form.Item {...field} name={[field.name, 'matchMode']} style={{ marginBottom: 0, width: 130 }}>
                      <Select
                        options={[
                          { label: t('pages.evaluation.matchExact'), value: 0 },
                          { label: t('pages.evaluation.matchContains'), value: 1 },
                        ]}
                      />
                    </Form.Item>
                    {canWrite && (
                      <Button danger icon={<DeleteOutlined />} onClick={() => remove(field.name)} />
                    )}
                  </Space>
                ))}
                <Button type="dashed" onClick={() => add({ input: '', expectedOutput: '', matchMode: 0 })} block>
                  {t('pages.evaluation.addCase')}
                </Button>
              </div>
            )}
          </Form.List>
        </Form>
      </Modal>

      <Modal
        title={t('pages.evaluation.run')}
        open={runModalOpen}
        onCancel={() => setRunModalOpen(false)}
        onOk={handleRun}
        confirmLoading={running}
        okText={t('pages.evaluation.run')}
        cancelText={t('pages.evaluation.cancel')}
      >
        <Form layout="vertical">
          <Form.Item label={t('pages.evaluation.runWorkflowLabel')} required>
            <Select
              placeholder={t('pages.evaluation.runWorkflowPlaceholder')}
              options={workflowOptions}
              value={selectedWorkflow}
              onChange={setSelectedWorkflow}
              showSearch
              optionFilterProp="label"
              notFoundContent={t('pages.evaluation.empty')}
            />
          </Form.Item>
          {running && <Text type="secondary">{t('pages.evaluation.running')}</Text>}
        </Form>
      </Modal>

      <Drawer
        title={t('pages.evaluation.reportTitle')}
        open={reportOpen}
        onClose={() => setReportOpen(false)}
        width={880}
      >
        {report && (
          <>
            <Descriptions column={3} style={{ marginBottom: 16 }}>
              <Descriptions.Item label={t('pages.evaluation.reportTotal')}>{report.total}</Descriptions.Item>
              <Descriptions.Item label={t('pages.evaluation.reportPassed')}>{report.passed}</Descriptions.Item>
              <Descriptions.Item label={t('pages.evaluation.reportScore')}>
                <Progress
                  type="circle"
                  percent={Math.round(report.score * 100)}
                  size={64}
                  status={report.passed === report.total ? 'success' : 'normal'}
                />
              </Descriptions.Item>
            </Descriptions>
            <Table
              rowKey={(_, i) => String(i)}
              columns={reportColumns}
              dataSource={report.cases}
              pagination={false}
              size="small"
            />
          </>
        )}
      </Drawer>
    </div>
  );
};

export default EvaluationDatasetsPage;
