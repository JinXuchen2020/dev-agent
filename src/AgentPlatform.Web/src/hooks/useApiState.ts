import { useCallback, useEffect, useState } from 'react';
import { getErrorMessage } from '../services/api';
import { useAppStore } from '../stores/appStore';

export interface ApiState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  retry: () => void;
}

/**
 * 统一异步数据加载状态：loading / error / data / retry。
 * 用于消除各页面 `.catch(() => {})` 静默吞错（O5）。
 * loader 在 deps 变化或 retry() 时重新执行；组件卸载后不再 setState。
 * F35：内部订阅 currentWorkspaceId —— 切换工作空间后所有经此 hook 加载的数据自动刷新
 * （决策 D5=A：状态驱动刷新，单点改 hook，无需逐页注入依赖）。
 */
export function useApiState<T>(loader: () => Promise<T>, deps: React.DependencyList = []): ApiState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const currentWorkspaceId = useAppStore((s) => s.currentWorkspaceId);

  const retry = useCallback(() => setReload((n) => n + 1), []);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    loader()
      .then((d) => {
        if (active) setData(d);
      })
      .catch((e: unknown) => {
        if (active) setError(getErrorMessage(e));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
    // loader 由调用方保证稳定或置于 deps；此处用 reload 显式触发重跑
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reload, currentWorkspaceId]);

  return { data, loading, error, retry };
}
