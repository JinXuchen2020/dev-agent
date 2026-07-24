import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { App as AntApp } from 'antd';
import LoginPage from '../LoginPage';
import * as api from '../../services/api';
import { useAppStore } from '../../stores/appStore';

vi.mock('../../services/api', () => ({
  loginRequest: vi.fn(),
}));

// antd 对双字中文按钮自动插入空格（"登录" → "登 录"），用去空白匹配避免脆弱。
const byText = (text: string) =>
  screen.getByText((content) => content.replace(/\s+/g, '') === text);

beforeEach(() => {
  vi.mocked(api.loginRequest).mockReset();
  useAppStore.setState({
    isAuthenticated: false,
    isDemo: false,
    userEmail: null,
    authBootstrapped: false,
  });
});

const renderLogin = () =>
  render(
    <MemoryRouter>
      <AntApp>
        <LoginPage />
      </AntApp>
    </MemoryRouter>,
  );

describe('LoginPage', () => {
  it('渲染登录表单（邮箱占位符 + 登录按钮）', () => {
    renderLogin();
    expect(screen.getByPlaceholderText('admin@acme.io')).toBeInTheDocument();
    expect(byText('登录')).toBeInTheDocument();
  });

  it('点击「演示会话」走 loginDemo，写入本地演示态', () => {
    renderLogin();
    fireEvent.click(byText('使用本地演示会话（无真实鉴权）'));
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(true);
    expect(s.isDemo).toBe(true);
    expect(s.userEmail).toBe('admin@acme.io');
  });

  it('登录失败（401）调用 loginRequest 且不进入已登录态', async () => {
    vi.mocked(api.loginRequest).mockRejectedValue({ response: { status: 401 } });
    renderLogin();
    fireEvent.click(byText('登录'));
    await waitFor(() => expect(api.loginRequest).toHaveBeenCalledTimes(1));
    expect(useAppStore.getState().isAuthenticated).toBe(false);
  });
});
