import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import StatusBadge from '../StatusBadge';

describe('StatusBadge', () => {
  it('renders the status text by default', () => {
    render(<StatusBadge status="active" />);
    expect(screen.getByText('active')).toBeInTheDocument();
  });

  it('renders a custom label when provided', () => {
    render(<StatusBadge status="running" label="运行中" />);
    expect(screen.getByText('运行中')).toBeInTheDocument();
    expect(screen.queryByText('running')).not.toBeInTheDocument();
  });

  it('does not throw on empty status', () => {
    const { container } = render(<StatusBadge status="" />);
    const span = container.querySelector('span');
    expect(span).not.toBeNull();
    expect(span?.textContent).toBe('');
  });
});
