import React from 'react';
import { Row, Col, Card, Statistic, Typography, Spin } from 'antd';
import { RobotOutlined, ApartmentOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { getAgents, getWorkflows, getExecutionLogs } from '../services/api';
import { useApiState } from '../hooks/useApiState';
import ErrorState from '../components/ErrorState';

const { Title } = Typography;

const DashboardPage: React.FC = () => {
  const { t } = useTranslation();
  const agents = useApiState(() => getAgents(), []);
  // 注：take 故意传 1 而非 0。后端列表端点控制器层 `take<1→400` 早于 handler 的 Math.Clamp，
  // 传 0 会被拒（Dashboard 只为取 totalCount，与 take 无关）。见 docs/learning/08-decision-log.md §8.13。
  const workflows = useApiState(() => getWorkflows({ take: 1 }), []);
  const success = useApiState(() => getExecutionLogs({ status: 'completed', take: 1 }), []);
  const failed = useApiState(() => getExecutionLogs({ status: 'failed', take: 1 }), []);

  const loading = agents.loading || workflows.loading || success.loading || failed.loading;
  const error = agents.error || workflows.error || success.error || failed.error;
  const retryAll = () => {
    agents.retry();
    workflows.retry();
    success.retry();
    failed.retry();
  };

  return (
    <div>
      <Title level={4}>{t('pages.dashboard.title')}</Title>
      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : error ? (
        <ErrorState message={t('errors.loadFailed')} description={error} onRetry={retryAll} />
      ) : (
        <Row gutter={16}>
          <Col span={6}>
            <Card>
              <Statistic title={t('pages.dashboard.activeAgents')} value={agents.data?.length ?? 0} prefix={<RobotOutlined />} />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic title={t('pages.dashboard.workflows')} value={workflows.data?.totalCount ?? 0} prefix={<ApartmentOutlined />} />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic
                title={t('pages.dashboard.successful')}
                value={success.data?.totalCount ?? 0}
                prefix={<CheckCircleOutlined />}
                valueStyle={{ color: '#3f8600' }}
              />
            </Card>
          </Col>
          <Col span={6}>
            <Card>
              <Statistic
                title={t('pages.dashboard.failed')}
                value={failed.data?.totalCount ?? 0}
                prefix={<CloseCircleOutlined />}
                valueStyle={{ color: '#cf1322' }}
              />
            </Card>
          </Col>
        </Row>
      )}
    </div>
  );
};

export default DashboardPage;
