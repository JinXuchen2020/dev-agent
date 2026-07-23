import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import ErrorState from '../ErrorState';

describe('ErrorState', () => {
  it('renders the error message', () => {
    render(<ErrorState message="加载失败" />);
    expect(screen.getByText('加载失败')).toBeInTheDocument();
  });

  it('renders retry button and calls onRetry when clicked', () => {
    const onRetry = vi.fn();
    render(<ErrorState message="加载失败" onRetry={onRetry} />);
    const btn = screen.getByRole('button', { name: /重试/ });
    expect(btn).toBeInTheDocument();
    fireEvent.click(btn);
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('does not render a retry button when onRetry is omitted', () => {
    render(<ErrorState message="加载失败" />);
    expect(screen.queryByRole('button', { name: /重试/ })).not.toBeInTheDocument();
  });
});
