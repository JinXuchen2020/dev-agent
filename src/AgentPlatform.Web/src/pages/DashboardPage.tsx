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
} from 'antd';
import {
  RobotOutlined,
  ApartmentOutlined,
  ThunderboltOutlined,
  CheckCircleOutlined,
  FundOutlined,
  FieldTimeOutlined,
} from '@ant-design/icons';
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  BarChart,
  Bar,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
} from 'recharts';
import { useTranslation } from 'react-i18next';
import { getDashboardSummary } from '../services/api';
import { useApiState } from '../hooks/useApiState';
import ErrorState from '../components/ErrorState';
import type { ExecutionDayBucket } from '../types';

const { Title } = Typography;

const DAY_MS = 24 * 60 * 60 * 1000;

function formatDay(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const mm = `${d.getMonth() + 1}`.padStart(2, '0');
  const dd = `${d.getDate()}`.padStart(2, '0');
  return `${mm}-${dd}`;
}

const DashboardPage: React.FC = () => {
  const { t } = useTranslation();
  const [range, setRange] = useState<number>(14);

  const summary = useApiState(() => {
    const to = new Date();
    const from = new Date(to.getTime() - range * DAY_MS);
    return getDashboardSummary({ from: from.toISOString(), to: to.toISOString() });
  }, [range]);

  const loading = summary.loading;
  const error = summary.error;
  const data = summary.data;
  const kpis = data?.kpis;

  const retryAll = () => summary.retry();

  const execData: ExecutionDayBucket[] = data?.executionsByDay ?? [];
  const tokenData = data?.tokenByDay ?? [];
  const convData = data?.conversationsByDay ?? [];
  const latencyData = data?.latencyByDay ?? [];
  const topWorkflows = data?.topWorkflows ?? [];

  const chartHeight = 280;

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
          {t('pages.dashboard.title')}
        </Title>
        <Segmented
          value={range}
          onChange={(v) => setRange(v as number)}
          options={[
            { label: t('pages.dashboard.last7Days'), value: 7 },
            { label: t('pages.dashboard.last14Days'), value: 14 },
            { label: t('pages.dashboard.last30Days'), value: 30 },
          ]}
        />
      </div>

      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : error ? (
        <ErrorState message={t('errors.loadFailed')} description={error} onRetry={retryAll} />
      ) : (
        <>
          {/* KPI 卡片（6） */}
          <Row gutter={[16, 16]}>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiActiveAgents')}
                  value={kpis?.activeAgents ?? 0}
                  prefix={<RobotOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiActiveWorkflows')}
                  value={kpis?.activeWorkflows ?? 0}
                  prefix={<ApartmentOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiTotalExecutions')}
                  value={kpis?.totalExecutions ?? 0}
                  prefix={<ThunderboltOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiSuccessRate')}
                  value={kpis?.successRate ?? 0}
                  precision={2}
                  suffix="%"
                  prefix={<CheckCircleOutlined />}
                  valueStyle={{ color: '#3f8600' }}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiTotalTokens')}
                  value={kpis?.totalTokens ?? 0}
                  prefix={<FundOutlined />}
                />
              </Card>
            </Col>
            <Col xs={12} md={8} lg={4}>
              <Card>
                <Statistic
                  title={t('pages.dashboard.kpiAvgLatency')}
                  value={kpis?.avgLatencyMs ?? 0}
                  precision={2}
                  prefix={<FieldTimeOutlined />}
                />
              </Card>
            </Col>
          </Row>

          {/* 图表 C1-C6 */}
          {execData.length === 0 && tokenData.length === 0 && convData.length === 0 ? (
            <Card style={{ marginTop: 16 }}>
              <Empty description={t('pages.dashboard.empty')} />
            </Card>
          ) : (
            <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
              {/* C1 每日执行趋势（堆叠柱：成功/失败/运行中） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.executionsTitle')}>
                  <ResponsiveContainer width="100%" height={chartHeight}>
                    <BarChart data={execData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tickFormatter={formatDay} />
                      <YAxis allowDecimals={false} />
                      <Tooltip labelFormatter={(v) => formatDay(String(v))} />
                      <Legend
                        formatter={(v) =>
                          v === 'completed'
                            ? t('pages.dashboard.legendCompleted')
                            : v === 'failed'
                              ? t('pages.dashboard.legendFailed')
                              : t('pages.dashboard.legendRunning')
                        }
                      />
                      <Bar dataKey="completed" stackId="a" fill="#3f8600" />
                      <Bar dataKey="failed" stackId="a" fill="#cf1322" />
                      <Bar dataKey="running" stackId="a" fill="#1677ff" />
                    </BarChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* C6 每日成功率（折线） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.successRateTitle')}>
                  <ResponsiveContainer width="100%" height={chartHeight}>
                    <LineChart data={execData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tickFormatter={formatDay} />
                      <YAxis domain={[0, 100]} unit="%" />
                      <Tooltip
                        labelFormatter={(v) => formatDay(String(v))}
                        formatter={(v: number) => [`${v}%`, t('pages.dashboard.kpiSuccessRate')]}
                      />
                      <Line type="monotone" dataKey="successRate" stroke="#3f8600" dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* C2 每日 Token 消耗（面积） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.tokensTitle')}>
                  <ResponsiveContainer width="100%" height={chartHeight}>
                    <AreaChart data={tokenData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tickFormatter={formatDay} />
                      <YAxis />
                      <Tooltip labelFormatter={(v) => formatDay(String(v))} />
                      <Area
                        type="monotone"
                        dataKey="totalTokens"
                        stroke="#1677ff"
                        fill="#1677ff"
                        fillOpacity={0.2}
                      />
                    </AreaChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* C3 每日会话量（柱） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.conversationsTitle')}>
                  <ResponsiveContainer width="100%" height={chartHeight}>
                    <BarChart data={convData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tickFormatter={formatDay} />
                      <YAxis allowDecimals={false} />
                      <Tooltip labelFormatter={(v) => formatDay(String(v))} />
                      <Bar dataKey="count" fill="#722ed1" />
                    </BarChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* C4 每日平均延迟（折线） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.latencyTitle')}>
                  <ResponsiveContainer width="100%" height={chartHeight}>
                    <LineChart data={latencyData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tickFormatter={formatDay} />
                      <YAxis unit="ms" />
                      <Tooltip
                        labelFormatter={(v) => formatDay(String(v))}
                        formatter={(v: number) => [`${v} ms`, t('pages.dashboard.kpiAvgLatency')]}
                      />
                      <Line type="monotone" dataKey="avgMs" stroke="#fa8c16" dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </Card>
              </Col>

              {/* C5 Top 工作流（横向柱） */}
              <Col xs={24} lg={12}>
                <Card title={t('pages.dashboard.topWorkflowsTitle')}>
                  {topWorkflows.length === 0 ? (
                    <Empty description={t('pages.dashboard.empty')} />
                  ) : (
                    <ResponsiveContainer width="100%" height={chartHeight}>
                      <BarChart
                        data={topWorkflows}
                        layout="vertical"
                        margin={{ left: 24, right: 16 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis type="number" allowDecimals={false} />
                        <YAxis
                          type="category"
                          dataKey="workflowName"
                          width={120}
                          tick={{ fontSize: 12 }}
                        />
                        <Tooltip />
                        <Bar dataKey="count" fill="#13c2c2" />
                      </BarChart>
                    </ResponsiveContainer>
                  )}
                </Card>
              </Col>
            </Row>
          )}
        </>
      )}
    </div>
  );
};

export default DashboardPage;
