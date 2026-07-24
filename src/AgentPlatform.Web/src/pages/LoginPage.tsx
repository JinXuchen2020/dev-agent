import React, { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Input, Button, message } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { loginRequest } from '../services/api';
import { useAppStore } from '../stores/appStore';
import { colors, radius, fontStack } from '../theme/tokens';

const LoginPage: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const loginReal = useAppStore((s) => s.loginReal);
  const loginDemo = useAppStore((s) => s.loginDemo);
  const from = (location.state as { from?: { pathname?: string } })?.from;
  const [email, setEmail] = useState('admin@acme.io');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);

  const handleLogin = async () => {
    setLoading(true);
    try {
      const res = await loginRequest({ email, password });
      loginReal(res.user);
      message.success('登录成功');
      navigate(from?.pathname || '/', { replace: true });
    } catch (e: unknown) {
      const status = (e as { response?: { status?: number } })?.response?.status;
      if (status === 401) {
        message.error('邮箱或密码错误');
      } else {
        message.error('登录失败，请确认后端已启动并支持用户登录');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleDemo = () => {
    loginDemo(email || 'admin@acme.io');
    message.warning('已使用本地演示会话（无真实鉴权）');
    navigate(from?.pathname || '/', { replace: true });
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        background: colors.canvas,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontFamily: fontStack,
        padding: 24,
      }}
    >
      <div
        style={{
          width: 420,
          background: colors.surface,
          borderRadius: 12,
          padding: 40,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 20,
          boxShadow: '0 8px 32px -8px rgba(0,0,0,0.08)',
          border: `1px solid ${colors.border}`,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <div style={{ width: 40, height: 40, borderRadius: 10, background: colors.accent }} />
          <span style={{ fontSize: 20, fontWeight: 700, color: colors.textPrimary }}>AgentPlatform</span>
        </div>

        <div style={{ textAlign: 'center' }}>
          <div style={{ fontSize: 26, fontWeight: 600, color: colors.textPrimary }}>欢迎回来</div>
          <div style={{ fontSize: 14, color: colors.textSecondary, marginTop: 6 }}>登录到您的租户管理后台</div>
        </div>

        <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 6 }}>
          <label style={{ fontSize: 13, fontWeight: 500, color: colors.textPrimary }}>邮箱</label>
          <Input
            size="large"
            prefix={<UserOutlined />}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="admin@acme.io"
          />
        </div>

        <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 6 }}>
          <label style={{ fontSize: 13, fontWeight: 500, color: colors.textPrimary }}>密码</label>
          <Input.Password
            size="large"
            prefix={<LockOutlined />}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="请输入密码"
            onPressEnter={handleLogin}
          />
        </div>

        <Button
          type="primary"
          size="large"
          block
          loading={loading}
          onClick={handleLogin}
          style={{ height: 48, borderRadius: radius.card, fontSize: 15, fontWeight: 600 }}
        >
          登录
        </Button>

        <Button
          type="link"
          size="small"
          block
          onClick={handleDemo}
          style={{ fontSize: 12 }}
        >
          使用本地演示会话（无真实鉴权）
        </Button>

        <div style={{ fontSize: 12, color: colors.textMuted, textAlign: 'center' }}>
          演示默认账号 admin@acme.io / Admin@123456（生产环境请修改）
        </div>
      </div>
    </div>
  );
};

export default LoginPage;
