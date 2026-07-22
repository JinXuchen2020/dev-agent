import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAppStore } from '../stores/appStore';

// 路由守卫：未登录（无 token）时重定向到 /login，并记录来源以便登录后跳回
const ProtectedRoute: React.FC = () => {
  const isAuthenticated = useAppStore((s) => s.isAuthenticated);
  const location = useLocation();

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }
  return <Outlet />;
};

export default ProtectedRoute;
