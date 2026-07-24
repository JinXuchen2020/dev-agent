import React, { useEffect } from 'react';
import { Routes, Route, useNavigate } from 'react-router-dom';
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
import KnowledgeBasesPage from './pages/KnowledgeBasesPage';
import KnowledgeBaseDetailPage from './pages/KnowledgeBaseDetailPage';
import ConversationsPage from './pages/ConversationsPage';
import ConversationDetailPage from './pages/ConversationDetailPage';
import ErrorBoundary from './components/ErrorBoundary';
import ProtectedRoute from './components/ProtectedRoute';
import LoginPage from './pages/LoginPage';
import NotFoundPage from './pages/NotFoundPage';
import { useAppStore } from './stores/appStore';

const App: React.FC = () => {
  const navigate = useNavigate();
  const bootstrapAuth = useAppStore((s) => s.bootstrapAuth);

  // Probe the backend for the current identity once on mount (cookie auth).
  useEffect(() => {
    void bootstrapAuth();
  }, [bootstrapAuth]);

  // SPA-safe 401 handling (O2): redirect to /login inside the router without a
  // full-page reload. Demo sessions skip the redirect (they intentionally have no cookie).
  useEffect(() => {
    const onUnauthorized = () => {
      const { isDemo } = useAppStore.getState();
      if (!isDemo) navigate('/login');
    };
    window.addEventListener('auth:unauthorized', onUnauthorized);
    return () => window.removeEventListener('auth:unauthorized', onUnauthorized);
  }, [navigate]);

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
        <ErrorBoundary>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={<ProtectedRoute />}>
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
                <Route path="/knowledge-bases" element={<KnowledgeBasesPage />} />
                <Route path="/knowledge-bases/:id" element={<KnowledgeBaseDetailPage />} />
                <Route path="/conversations" element={<ConversationsPage />} />
                <Route path="/conversations/:id" element={<ConversationDetailPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Route>
          </Routes>
        </ErrorBoundary>
      </AntApp>
    </ConfigProvider>
  );
};

export default App;
