import { create } from 'zustand';

const TOKEN_KEY = 'auth_token';

interface AppState {
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
  isAuthenticated: boolean;
  userEmail: string | null;
  login: (email: string) => void;
  logout: () => void;
}

export const useAppStore = create<AppState>((set) => ({
  sidebarCollapsed: false,
  toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
  isAuthenticated: typeof window !== 'undefined' && !!localStorage.getItem(TOKEN_KEY),
  userEmail: null,
  login: (email: string) => set({ isAuthenticated: true, userEmail: email }),
  logout: () => {
    if (typeof window !== 'undefined') localStorage.removeItem(TOKEN_KEY);
    set({ isAuthenticated: false, userEmail: null });
  },
}));
