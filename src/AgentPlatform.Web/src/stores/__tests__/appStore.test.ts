import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useAppStore } from '../appStore';
import * as api from '../../services/api';
import type { AuthUser } from '../../types';

const AUTH_USER: AuthUser = {
  id: 'u1',
  email: 'admin@acme.io',
  role: 'admin',
  tenantId: 't1',
};

vi.mock('../../services/api', () => ({
  getAuthMe: vi.fn(),
  logoutRequest: vi.fn(),
  // F35: appStore 从 api 模块导入 workspace 持久化键（单一事实来源），mock 需同步提供。
  WORKSPACE_STORAGE_KEY: 'app-workspace-id',
}));

beforeEach(() => {
  vi.mocked(api.getAuthMe).mockReset();
  vi.mocked(api.logoutRequest).mockReset();
  useAppStore.setState({
    isAuthenticated: false,
    authBootstrapped: false,
    isDemo: false,
    userEmail: null,
  });
});

describe('appStore 鉴权态迁移', () => {
  it('bootstrapAuth 成功：写入身份并标记 bootstrapped', async () => {
    vi.mocked(api.getAuthMe).mockResolvedValue(AUTH_USER);
    await useAppStore.getState().bootstrapAuth();
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(true);
    expect(s.isDemo).toBe(false);
    expect(s.userEmail).toBe('admin@acme.io');
    expect(s.authBootstrapped).toBe(true);
  });

  it('bootstrapAuth 失败：保持未登录并标记 bootstrapped（避免首屏误跳）', async () => {
    vi.mocked(api.getAuthMe).mockRejectedValue(new Error('401'));
    await useAppStore.getState().bootstrapAuth();
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.userEmail).toBeNull();
    expect(s.authBootstrapped).toBe(true);
  });

  it('loginReal：真实登录态', () => {
    useAppStore.getState().loginReal(AUTH_USER);
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(true);
    expect(s.isDemo).toBe(false);
    expect(s.userEmail).toBe('admin@acme.io');
  });

  it('loginDemo：本地演示态（无 cookie）', () => {
    useAppStore.getState().loginDemo('demo@local');
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(true);
    expect(s.isDemo).toBe(true);
    expect(s.userEmail).toBe('demo@local');
  });

  it('logout：清空身份并调用 logoutRequest', async () => {
    vi.mocked(api.logoutRequest).mockResolvedValue(undefined);
    useAppStore.getState().loginReal(AUTH_USER);
    useAppStore.getState().logout();
    const s = useAppStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.isDemo).toBe(false);
    expect(s.userEmail).toBeNull();
    expect(api.logoutRequest).toHaveBeenCalledTimes(1);
  });
});
