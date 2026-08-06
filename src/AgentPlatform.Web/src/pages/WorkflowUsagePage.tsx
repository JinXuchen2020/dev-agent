import React, { useState } from 'react';
import {
  Row,
  Col,
  Card,
  Statistic,
  Typography,
  Spin,
  Segmented,
  Empty,
  Table,
} from 'antd';
import { ThunderboltOutlined, CheckCircleOutlined, FundOutlined, FieldTimeOutlined } from '@ant-design/icons';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
} from 'recharts';
import { useTranslation } from 'react-i18next';
import { getWorkflowUsage } from '../services/api';
import { useApiState } from '../hooks/useApiState';
import ErrorState from '../components/ErrorState';
import type { WorkflowUsageDto } from '../types';

const { Title } = Typography;

const DAY_MS = 24 * 60 * 60 * 1000;

const WorkflowUsagePage: React.FC = () => {
  const { t } = useTranslation();
  const [range, setRange] = useState<number>(14);

  const usage = useApiState(() => {
    const to = new Date();
    const from = new Date(to.getTime() - range * DAY_MS);
    return getWorkflowUsage({ from: from.toISOString(), to: to.toISOString() });
  }, [range]);

  const loading = usage.loading;
  const error = usage.error;
  const items: WorkflowUsageDto[] = usage.data?.items ?? [];

  const retryAll = () => usage.retry();

  // 跨工作流汇总 KPI（按执行次数加权成功率不在此展示，仅展示简单聚合）。
  const totalExecutions = items.reduce((s, it) => s + it.executions, 0);
  const totalTokens = items.reduce((s, it) => s + it.totalTokens, 0);
  const completedAll = items.reduce((s, it) => s + it.completed, 0);
  const failedAll = items.reduce((s, it) => s + it.failed, 0);
  const aggSuccessRate = completedAll + failedAll > 0 ? (completedAll * 100) / (completedAll + failedAll) : 0;
  const weightedLatency =
    totalExecutions > 0
      ? items.reduce((s, it) => s + it.avgLatencyMs * it.executions, 0) / totalExecutions
      : 0;

  const chartHeight = 320;
  const sortedByExec = [...items].sort((a, b) => b.executions - a.executions);

  const columns = [
    {
      title: t('pages.usage.colWorkflow'),
      dataIndex: 'workflowName',
      key: 'workflowName',
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.workflowName.localeCompare(b.workflowName),
    },
    {
      title: t('pages.usage.colExecutions'),
      dataIndex: 'executions',
      key: 'executions',
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.executions - b.executions,
      defaultSortOrder: 'descend' as const,
    },
    {
      title: t('pages.usage.colCompleted'),
      dataIndex: 'completed',
      key: 'completed',
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.completed - b.completed,
    },
    {
      title: t('pages.usage.colFailed'),
      dataIndex: 'failed',
      key: 'failed',
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.failed - b.failed,
    },
    {
      title: t('pages.usage.colSuccessRate'),
      dataIndex: 'successRate',
      key: 'successRate',
      render: (v: number) => `${v.toFixed(2)}%`,
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.successRate - b.successRate,
    },
    {
      title: t('pages.usage.colAvgLatency'),
      dataIndex: 'avgLatencyMs',
      key: 'avgLatencyMs',
      render: (v: number) => `${v.toFixed(2)} ms`,
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.avgLatencyMs - b.avgLatencyMs,
    },
    {
      title: t('pages.usage.colTotalTokens'),
      dataIndex: 'totalTokens',
      key: 'totalTokens',
      sorter: (a: WorkflowUsageDto, b: WorkflowUsageDto) => a.totalTokens - b.totalTokens,
    },
  ];

  return (
    <div>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
          marginBottom: 16,
        }}
      >
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.usage.title')}
        </Title>
        <Segmented
          value={range}
          onChange={(v) => setRange(v as number)}
          options={[
            { label: t('pages.usage.last7Days'), value: 7 },
            { label: t('pages.usage.last14Days'), value: 14 },
            { label: t('pages.usage.last30Days'), value: 30 },
          ]}
        />
      </div>

      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : error ? (
        <ErrorState message={t('errors.loadFailed')} description={error} onRetry={retryAll} />
      ) : items.length === 0 ? (
        <Card>
          <Empty description={t('pages.usage.empty')} />
        </Card>
      ) : (
        <>
          {/* 汇总 KPI */}
          <Row gutter={[16, 16]}>
            <Col xs={12} md={8} lg={6}>
              <Card>
                <Statistic
                  title={t('pages.usage.colExecutions')}
                  value={totalExecutions}
                  prefix={<ThunderboltOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={6}>
              <Card>
                <Statistic
                  title={t('pages.usage.kpiSuccessRate')}
                  value={aggSuccessRate}
                  precision={2}
                  suffix="%"
                  prefix={<CheckCircleOutlined />}
                  valueStyle={{ color: '#3f8600' }}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={6}>
              <Card>
                <Statistic
                  title={t('pages.usage.colTotalTokens')}
                  value={totalTokens}
                  prefix={<FundOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={6}>
              <Card>
                <Statistic
                  title={t('pages.usage.kpiAvgLatency')}
                  value={weightedLatency}
                  precision={2}
                  prefix={<FieldTimeOutlined />}
                />
              </Card>
            </Col>
          </Row>

          {/* 各工作流执行次数（横向柱） */}
          <Card title={t('pages.usage.chartTitle')} style={{ marginTop: 16 }}>
            <ResponsiveContainer width="100%" height={chartHeight}>
              <BarChart
                data={sortedByExec}
                layout="vertical"
                margin={{ left: 24, right: 16 }}
              >
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis type="number" allowDecimals={false} />
                <YAxis type="category" dataKey="workflowName" width={160} tick={{ fontSize: 12 }} />
                <Tooltip />
                <Bar dataKey="executions" fill="#1677ff" />
              </BarChart>
            </ResponsiveContainer>
          </Card>

          {/* 明细表 */}
          <Card style={{ marginTop: 16 }}>
            <Table<WorkflowUsageDto>
              rowKey="workflowId"
              columns={columns}
              dataSource={items}
              pagination={items.length > 10 ? { pageSize: 10 } : false}
              size="middle"
            />
          </Card>
        </>
      )}
    </div>
  );
};

export default WorkflowUsagePage;
