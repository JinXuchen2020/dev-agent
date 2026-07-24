import { describe, it, expect, vi } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useApiState } from '../useApiState';

describe('useApiState 异步加载态', () => {
  it('loader resolve：data 填充、loading 结束、error 为空', async () => {
    const loader = vi.fn().mockResolvedValue({ ok: true });
    const { result } = renderHook(() => useApiState(loader));
    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.data).toEqual({ ok: true });
    expect(result.current.error).toBeNull();
  });

  it('loader reject：error 填充且 loading 结束', async () => {
    const loader = vi.fn().mockRejectedValue(new Error('boom'));
    const { result } = renderHook(() => useApiState(loader));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.error).toBe('boom');
    expect(result.current.data).toBeNull();
  });

  it('retry：重新执行 loader', async () => {
    const loader = vi
      .fn()
      .mockResolvedValueOnce('first')
      .mockResolvedValueOnce('second');
    const { result } = renderHook(() => useApiState(loader, []));
    await waitFor(() => expect(result.current.data).toBe('first'));
    act(() => result.current.retry());
    await waitFor(() => expect(result.current.data).toBe('second'));
    expect(loader).toHaveBeenCalledTimes(2);
  });

  it('卸载后不再 setState（不抛错、loader 仅调用一次）', async () => {
    const loader = vi.fn().mockResolvedValue('x');
    const { unmount } = renderHook(() => useApiState(loader));
    unmount();
    await new Promise((r) => setTimeout(r, 10));
    expect(loader).toHaveBeenCalledTimes(1);
  });
});
