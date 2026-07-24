import { create } from 'zustand';
import { decodeJwt } from '../services/api';

const TOKEN_KEY = 'auth_token';

interface AppState {
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
  isAuthenticated: boolean;
  userEmail: string | null;
  userRole: string | null;
  login: (token?: string, email?: string) => void;
  logout: () => void;
}

// 从 JWT 解码真实身份；无令牌或解码失败时回退到登录输入框邮箱。
function identityFromToken(token: string | null, fallbackEmail?: string): {
  email: string | null;
  role: string | null;
} {
  if (!token) return { email: fallbackEmail ?? null, role: null };
  const claims = decodeJwt(token);
  const email = claims?.email ?? claims?.sub ?? claims?.name ?? claims?.unique_name ?? fallbackEmail ?? null;
  const rawRole = claims?.role;
  const role = Array.isArray(rawRole) ? (rawRole[0] ?? null) : (rawRole ?? null);
  return { email, role };
}

const initialToken = typeof window !== 'undefined' ? localStorage.getItem(TOKEN_KEY) : null;
const initialIdentity = identityFromToken(initialToken);

export const useAppStore = create<AppState>((set) => ({
  sidebarCollapsed: false,
  toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
  isAuthenticated: !!initialToken,
  // 刷新后从持久化令牌回填真实身份（O4），不再恒为 null。
  userEmail: initialIdentity.email,
  userRole: initialIdentity.role,
  login: (token, email) => {
    const identity = identityFromToken(token ?? null, email);
    set({ isAuthenticated: true, userEmail: identity.email, userRole: identity.role });
  },
  logout: () => {
    if (typeof window !== 'undefined') localStorage.removeItem(TOKEN_KEY);
    set({ isAuthenticated: false, userEmail: null, userRole: null });
  },
}));
