import React, { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Input, Button, message } from 'antd';
import { devLogin } from '../services/api';
import { useAppStore } from '../stores/appStore';
import { colors, radius, fontStack } from '../theme/tokens';

const LoginPage: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const login = useAppStore((s) => s.login);
  const from = (location.state as { from?: { pathname?: string } })?.from;
  const [email, setEmail] = useState('admin@acme.io');
  const [loading, setLoading] = useState(false);

  const handleLogin = async () => {
    setLoading(true);
    try {
      // 优先走后端 dev-login 换取真实 JWT（需 Security:DevLoginEnabled=true）
      const res = await devLogin({ role: 'Admin', userId: email });
      localStorage.setItem('auth_token', res.token);
      login(email);
      message.success('登录成功');
      navigate(from?.pathname || '/', { replace: true });
    } catch {
      // 后端未开启 dev-login 时，降级为本地演示登录
      login(email);
      message.warning('后端未开启 Dev Login，已使用本地演示会话');
      navigate(from?.pathname || '/', { replace: true });
    } finally {
      setLoading(false);
    }
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
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="admin@acme.io"
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

        <div style={{ fontSize: 12, color: colors.textMuted }}>开发演示登录：admin@acme.io（免密，后端 Dev Login 仅校验邮箱）</div>
      </div>
    </div>
  );
};

export default LoginPage;
