import React, { lazy, Suspense, useEffect, useState } from 'react';
import { Routes, Route, useNavigate } from 'react-router-dom';
import { ConfigProvider, App as AntApp, Spin } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import enUS from 'antd/locale/en_US';
import dayjs from 'dayjs';
import 'dayjs/locale/zh-cn';
import 'dayjs/locale/en';
import AppLayout from './layouts/AppLayout';
import ErrorBoundary from './components/ErrorBoundary';
import ProtectedRoute from './components/ProtectedRoute';
import { useAppStore } from './stores/appStore';
import { i18n } from './locales';
import { SUPPORTED_LOCALES } from './locales/config';

// 路由级按需加载（O6）：壳层（AppLayout / ProtectedRoute / ErrorBoundary）保持 eager，
// 页面组件拆为独立 chunk，配合 vite.config.ts 的 manualChunks 供应商分包。
const LoginPage = lazy(() => import('./pages/LoginPage'));
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const AgentsPage = lazy(() => import('./pages/AgentsPage'));
const WorkflowsPage = lazy(() => import('./pages/WorkflowsPage'));
const WorkflowDetailPage = lazy(() => import('./pages/WorkflowDetailPage'));
const WorkflowCanvasPage = lazy(() => import('./pages/WorkflowCanvasPage'));
const AgentRolesPage = lazy(() => import('./pages/AgentRolesPage'));
const AgentConfigurationsPage = lazy(() => import('./pages/AgentConfigurationsPage'));
const CredentialSettingsPage = lazy(() => import('./pages/CredentialSettingsPage'));
const ExecutionLogsPage = lazy(() => import('./pages/ExecutionLogsPage'));
const ExecutionLogDetailPage = lazy(() => import('./pages/ExecutionLogDetailPage'));
const KnowledgeBasesPage = lazy(() => import('./pages/KnowledgeBasesPage'));
const KnowledgeBaseDetailPage = lazy(() => import('./pages/KnowledgeBaseDetailPage'));
const ConversationsPage = lazy(() => import('./pages/ConversationsPage'));
const ConversationDetailPage = lazy(() => import('./pages/ConversationDetailPage'));
const ResearchPage = lazy(() => import('./pages/ResearchPage'));
const NotFoundPage = lazy(() => import('./pages/NotFoundPage'));

const App: React.FC = () => {
  const navigate = useNavigate();
  const bootstrapAuth = useAppStore((s) => s.bootstrapAuth);

  // F15 · Antd + dayjs 区域随当前语言联动。
  const resolveAntdLocale = (lng: string) =>
    SUPPORTED_LOCALES.includes(lng as (typeof SUPPORTED_LOCALES)[number]) && lng === 'en-US'
      ? enUS
      : zhCN;
  const [antdLocale, setAntdLocale] = useState(() => resolveAntdLocale(i18n.language));

  // Probe the backend for the current identity once on mount (cookie auth).
  useEffect(() => {
    void bootstrapAuth();
  }, [bootstrapAuth]);

  // F15 · 语言切换时同步 Antd 与 dayjs 区域（日期选择器 / 分页等组件自带文案）。
  useEffect(() => {
    const onLanguageChanged = (lng: string) => {
      setAntdLocale(resolveAntdLocale(lng));
      dayjs.locale(lng === 'en-US' ? 'en' : 'zh-cn');
    };
    i18n.on('languageChanged', onLanguageChanged);
    // 初始化 dayjs 区域以匹配初始语言。
    dayjs.locale(i18n.language === 'en-US' ? 'en' : 'zh-cn');
    return () => {
      i18n.off('languageChanged', onLanguageChanged);
    };
  }, []);

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
      locale={antdLocale}
      theme={{
        token: {
          colorPrimary: '#1677ff',
        },
      }}
    >
      <AntApp>
        <ErrorBoundary>
          <Suspense
            fallback={
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'center',
                  alignItems: 'center',
                  height: '100vh',
                }}
              >
                <Spin size="large" />
              </div>
            }
          >
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
                  <Route path="/credentials" element={<CredentialSettingsPage />} />
                  <Route path="/execution-logs" element={<ExecutionLogsPage />} />
                  <Route path="/execution-logs/:id" element={<ExecutionLogDetailPage />} />
                  <Route path="/knowledge-bases" element={<KnowledgeBasesPage />} />
                  <Route path="/knowledge-bases/:id" element={<KnowledgeBaseDetailPage />} />
                  <Route path="/conversations" element={<ConversationsPage />} />
                  <Route path="/conversations/:id" element={<ConversationDetailPage />} />
                  <Route path="/research" element={<ResearchPage />} />
                  <Route path="*" element={<NotFoundPage />} />
                </Route>
              </Route>
            </Routes>
          </Suspense>
        </ErrorBoundary>
      </AntApp>
    </ConfigProvider>
  );
};

export default App;
