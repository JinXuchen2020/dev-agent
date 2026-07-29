import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import EntityCardGrid from '../EntityCardGrid';

describe('EntityCardGrid', () => {
  const items = [
    { id: '1', name: 'A' },
    { id: '2', name: 'B' },
  ];

  it('renders one card per item via renderCard', () => {
    render(
      <EntityCardGrid
        items={items}
        rowKey={(i) => i.id}
        renderCard={(i) => <div data-testid="card">{i.name}</div>}
      />,
    );
    expect(screen.getAllByTestId('card')).toHaveLength(2);
    expect(screen.getByText('A')).toBeInTheDocument();
    expect(screen.getByText('B')).toBeInTheDocument();
  });

  it('renders empty state with default text when no items', () => {
    render(
      <EntityCardGrid
        items={[] as Array<{ id: string; name: string }>}
        renderCard={(i) => <div>{i.name}</div>}
      />,
    );
    expect(screen.getByText('暂无数据')).toBeInTheDocument();
  });

  it('renders custom empty text', () => {
    render(
      <EntityCardGrid
        items={[] as Array<{ id: string; name: string }>}
        emptyText="没有内容"
        renderCard={(i) => <div>{i.name}</div>}
      />,
    );
    expect(screen.getByText('没有内容')).toBeInTheDocument();
  });

  it('renders skeleton loading state', () => {
    const { container } = render(
      <EntityCardGrid
        loading
        items={[] as Array<{ id: string; name: string }>}
        renderCard={(i) => <div>{i.name}</div>}
      />,
    );
    expect(container.querySelectorAll('.ant-skeleton').length).toBeGreaterThan(0);
  });

  it('calls onItemClick with the item when a card is clicked', () => {
    const onClick = vi.fn();
    render(
      <EntityCardGrid
        items={items}
        rowKey={(i) => i.id}
        onItemClick={onClick}
        renderCard={(i) => <div data-testid="card">{i.name}</div>}
      />,
    );
    fireEvent.click(screen.getByText('A'));
    expect(onClick).toHaveBeenCalledWith(items[0]);
  });

  it('does not attach click handler when onItemClick is omitted', () => {
    render(
      <EntityCardGrid
        items={items}
        rowKey={(i) => i.id}
        renderCard={(i) => <div data-testid="card">{i.name}</div>}
      />,
    );
    fireEvent.click(screen.getByText('B'));
    expect(screen.getAllByTestId('card')).toHaveLength(2);
  });

  it('does not fire onItemClick when an interactive child (button) is clicked', () => {
    const onClick = vi.fn();
    const onButton = vi.fn();
    render(
      <EntityCardGrid
        items={items}
        rowKey={(i) => i.id}
        onItemClick={onClick}
        renderCard={(i) => (
          <div data-testid="card">
            {i.name}
            <button onClick={onButton}>action</button>
          </div>
        )}
      />,
    );
    fireEvent.click(screen.getAllByText('action')[0]);
    expect(onButton).toHaveBeenCalledTimes(1);
    expect(onClick).not.toHaveBeenCalled();
  });
});
