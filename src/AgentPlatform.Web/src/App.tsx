import React from 'react';
import { Routes, Route } from 'react-router-dom';
import { ConfigProvider, App as AntApp } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import AppLayout from './layouts/AppLayout';
import DashboardPage from './pages/DashboardPage';
import AgentsPage from './pages/AgentsPage';
import WorkflowsPage from './pages/WorkflowsPage';
import WorkflowDetailPage from './pages/WorkflowDetailPage';
import WorkflowCanvasPage from './pages/WorkflowCanvasPage';
import AgentRolesPage from './pages/AgentRolesPage';
import AgentConfigurationsPage from './pages/AgentConfigurationsPage';
import ExecutionLogsPage from './pages/ExecutionLogsPage';
import ExecutionLogDetailPage from './pages/ExecutionLogDetailPage';

const App: React.FC = () => {
  return (
    <ConfigProvider
      locale={zhCN}
      theme={{
        token: {
          colorPrimary: '#1677ff',
        },
      }}
    >
      <AntApp>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/agents" element={<AgentsPage />} />
            <Route path="/workflows" element={<WorkflowsPage />} />
          <Route path="/workflows/new" element={<WorkflowCanvasPage />} />
          <Route path="/workflows/:id" element={<WorkflowDetailPage />} />
          <Route path="/workflows/:id/edit" element={<WorkflowCanvasPage />} />
            <Route path="/agent-roles" element={<AgentRolesPage />} />
            <Route path="/agent-configurations" element={<AgentConfigurationsPage />} />
            <Route path="/execution-logs" element={<ExecutionLogsPage />} />
            <Route path="/execution-logs/:id" element={<ExecutionLogDetailPage />} />
          </Route>
        </Routes>
      </AntApp>
    </ConfigProvider>
  );
};

export default App;
