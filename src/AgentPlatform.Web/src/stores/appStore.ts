import { create } from 'zustand';
import { getAuthMe, logoutRequest } from '../services/api';
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
  userRole: string | null;
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
  bootstrapAuth: async () => {
    try {
      const user = await getAuthMe();
      set({
        isAuthenticated: true,
        isDemo: false,
        userEmail: user.email,
        userRole: user.role,
        authBootstrapped: true,
      });
    } catch {
      set({
        isAuthenticated: false,
        isDemo: false,
        userEmail: null,
        userRole: null,
        authBootstrapped: true,
      });
    }
  },
  loginReal: (user) =>
    set({
      isAuthenticated: true,
      isDemo: false,
      userEmail: user.email,
      userRole: user.role,
      authBootstrapped: true,
    }),
  loginDemo: (email) =>
    set({
      isAuthenticated: true,
      isDemo: true,
      userEmail: email,
      userRole: null,
      authBootstrapped: true,
    }),
  logout: () => {
    // Best-effort cookie clear; ignore network errors.
    void logoutRequest().catch(() => undefined);
    set({ isAuthenticated: false, isDemo: false, userEmail: null, userRole: null });
  },
}));
