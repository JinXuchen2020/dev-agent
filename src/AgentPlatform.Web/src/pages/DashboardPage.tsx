import React, { useEffect, useState } from 'react';
import { Row, Col, Card, Statistic, Typography } from 'antd';
import { RobotOutlined, ApartmentOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import { getAgents, getWorkflows, getExecutionLogs } from '../services/api';

const { Title } = Typography;

const DashboardPage: React.FC = () => {
  const [agentCount, setAgentCount] = useState(0);
  const [workflowCount, setWorkflowCount] = useState(0);
  const [successCount, setSuccessCount] = useState(0);
  const [failedCount, setFailedCount] = useState(0);

  useEffect(() => {
    Promise.all([
      getAgents().then((d) => setAgentCount(d.length)).catch(() => {}),
      getWorkflows({ take: 0 }).then((d) => setWorkflowCount(d.totalCount)).catch(() => {}),
      getExecutionLogs({ status: 'completed', take: 0 }).then((d) => setSuccessCount(d.totalCount)).catch(() => {}),
      getExecutionLogs({ status: 'failed', take: 0 }).then((d) => setFailedCount(d.totalCount)).catch(() => {}),
    ]);
  }, []);

  return (
    <div>
      <Title level={4}>Dashboard</Title>
      <Row gutter={16}>
        <Col span={6}>
          <Card><Statistic title="Active Agents" value={agentCount} prefix={<RobotOutlined />} /></Card>
        </Col>
        <Col span={6}>
          <Card><Statistic title="Workflows" value={workflowCount} prefix={<ApartmentOutlined />} /></Card>
        </Col>
        <Col span={6}>
          <Card><Statistic title="Successful" value={successCount} prefix={<CheckCircleOutlined />} valueStyle={{ color: '#3f8600' }} /></Card>
        </Col>
        <Col span={6}>
          <Card><Statistic title="Failed" value={failedCount} prefix={<CloseCircleOutlined />} valueStyle={{ color: '#cf1322' }} /></Card>
        </Col>
      </Row>
    </div>
  );
};

export default DashboardPage;
