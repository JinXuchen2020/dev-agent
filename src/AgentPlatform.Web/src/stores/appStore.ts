import { create } from 'zustand';
import { getAuthMe, logoutRequest, WORKSPACE_STORAGE_KEY } from '../services/api';
import type { AuthUser } from '../types';

interface AppState {
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
  isAuthenticated: boolean;
  // False until the initial /auth/me probe completes (avoids redirect flicker).
  authBootstrapped: boolean;
  // Demo mode = local session without a real backend user (no auth cookie).
  isDemo: boolean;
  userEmail: string | null;
  // Current user's role (from GET /auth/me). Used for RBAC gating in the UI
  // (e.g. only Admins may create agents). Backend remains the source of truth.
  userRole: string | null;
  // F35: 当前活跃工作空间 Id（null = 未选择，请求不带 header，由后端 claim/默认兜底）。
  currentWorkspaceId: string | null;
  // 设置当前工作空间并持久化（WorkspaceSwitcher 切换时调用；useApiState 订阅此值触发全站刷新）。
  setCurrentWorkspaceId: (id: string | null) => void;
  // Probe the backend for the current identity (called once on app mount).
  bootstrapAuth: () => Promise<void>;
  // Real login: identity comes from the backend response.
  loginReal: (user: AuthUser) => void;
  // Demo login: local-only session, no cookie.
  loginDemo: (email: string) => void;
  logout: () => void;
}

export const useAppStore = create<AppState>((set) => ({
  sidebarCollapsed: false,
  toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
  isAuthenticated: false,
  authBootstrapped: false,
  isDemo: false,
  userEmail: null,
  userRole: null,
  currentWorkspaceId: typeof localStorage !== 'undefined' ? localStorage.getItem(WORKSPACE_STORAGE_KEY) : null,
  setCurrentWorkspaceId: (id) => {
    if (typeof localStorage !== 'undefined') {
      if (id) localStorage.setItem(WORKSPACE_STORAGE_KEY, id);
      else localStorage.removeItem(WORKSPACE_STORAGE_KEY);
    }
    set({ currentWorkspaceId: id });
  },
  bootstrapAuth: async () => {
    try {
      const user = await getAuthMe();
      set((state) => {
        // F35: 首次 bootstrap 时，若本地无持久化选择则采用后端 claim（租户默认工作空间）。
        const wsId = state.currentWorkspaceId ?? user.currentWorkspaceId ?? null;
        if (typeof localStorage !== 'undefined') {
          if (wsId) localStorage.setItem(WORKSPACE_STORAGE_KEY, wsId);
        }
        return {
          isAuthenticated: true,
          isDemo: false,
          userEmail: user.email,
          userRole: user.role,
          authBootstrapped: true,
          currentWorkspaceId: wsId,
        };
      });
    } catch {
      set({
        isAuthenticated: false,
        isDemo: false,
        userEmail: null,
        userRole: null,
        authBootstrapped: true,
        currentWorkspaceId: null,
      });
    }
  },
  loginReal: (user) =>
    set((state) => {
      // F35: 登录响应携带租户默认工作空间；保留用户此前在本浏览器的选择（若有）。
      const wsId = state.currentWorkspaceId ?? user.currentWorkspaceId ?? null;
      if (typeof localStorage !== 'undefined') {
        if (wsId) localStorage.setItem(WORKSPACE_STORAGE_KEY, wsId);
      }
      return {
        isAuthenticated: true,
        isDemo: false,
        userEmail: user.email,
        userRole: user.role,
        authBootstrapped: true,
        currentWorkspaceId: wsId,
      };
    }),
  loginDemo: (email) =>
    set({
      isAuthenticated: true,
      isDemo: true,
      userEmail: email,
      userRole: 'admin',
      authBootstrapped: true,
    }),
  logout: () => {
    // Best-effort cookie clear; ignore network errors.
    void logoutRequest().catch(() => undefined);
    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem(WORKSPACE_STORAGE_KEY);
    }
    set({ isAuthenticated: false, isDemo: false, userEmail: null, currentWorkspaceId: null });
  },
}));
