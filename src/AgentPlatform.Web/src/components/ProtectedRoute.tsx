import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAppStore } from '../stores/appStore';

// 路由守卫：未登录（无有效会话 cookie 且非 demo）时重定向到 /login。
// 等待首次 /auth/me 探活完成（authBootstrapped）再决策，避免刷新瞬间误跳。
const ProtectedRoute: React.FC = () => {
  const isAuthenticated = useAppStore((s) => s.isAuthenticated);
  const bootstrapped = useAppStore((s) => s.authBootstrapped);
  const location = useLocation();

  if (!bootstrapped) return null;
  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }
  return <Outlet />;
};

export default ProtectedRoute;
