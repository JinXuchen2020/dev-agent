import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import NotFoundPage from '../NotFoundPage';

describe('NotFoundPage', () => {
  it('渲染 404 文案与返回首页按钮', () => {
    render(
      <MemoryRouter>
        <NotFoundPage />
      </MemoryRouter>,
    );
    expect(screen.getByText('404')).toBeInTheDocument();
    expect(
      screen.getByText('抱歉，您访问的页面不存在。'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: '返回首页' }),
    ).toBeInTheDocument();
  });
});
