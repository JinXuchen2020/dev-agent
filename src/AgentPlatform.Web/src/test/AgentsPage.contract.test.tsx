import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import AgentsPage from '../pages/AgentsPage';
import * as api from '../services/api';
import type { Agent } from '../types';

// 完全按照后端 AgentResponse 的真实形状构造（camelCase、嵌套 role.roleCode、
// modelEndpoint.modelId、status 小写、systemPrompt、createdAt）。
// 这一层就是“API 契约”。如果前端列映射错位（读成扁平 roleCode /
// 缺 modelEndpoint / 缺 status 与 systemPrompt），表格会渲染成 '-' 或空白，
// 下面的断言就会失败——这正是此前 QA 漏掉的回归。
const SAMPLE: Agent[] = [
  {
    id: 'a1',
    name: '文档摘要助手',
    role: { roleCode: 'developer' },
    modelEndpoint: { modelId: 'gpt-4o' },
    systemPrompt: '你是一个文档摘要助手',
    status: 'active',
    createdAt: '2026-07-01T10:00:00Z',
  },
];

vi.mock('../services/api', () => ({
  getAgents: vi.fn(),
  getAgentRoles: vi.fn(),
  createAgent: vi.fn(),
}));

beforeEach(() => {
  vi.mocked(api.getAgents).mockResolvedValue(SAMPLE);
  vi.mocked(api.getAgentRoles).mockResolvedValue([]);
});

describe('AgentsPage 列映射契约', () => {
  it('把 API 返回的真实字段渲染进表格（不出现占位符 "-" 或空白）', async () => {
    render(<AgentsPage />);

    // 等待列表异步加载完成
    await waitFor(() => expect(screen.getByText('文档摘要助手')).toBeInTheDocument());

    // 角色列：必须出现 roleCode，而不是曾经的 '-'
    expect(screen.getByText('developer')).toBeInTheDocument();

    // Model 列：必须出现 modelEndpoint.modelId，而不是曾经的 '-'
    expect(screen.getByText('gpt-4o')).toBeInTheDocument();

    // System Prompt 列：必须出现真实内容，而不是曾经的空白
    expect(screen.getByText('你是一个文档摘要助手')).toBeInTheDocument();

    // 状态列：必须渲染出 status（徽章文字），而不是曾经的 undefined→空
    expect(screen.getByText('active')).toBeInTheDocument();

    // 创建时间列：必须渲染出时间，而不是曾经的空白
    expect(screen.getByText(/2026\/7\/1|2026-07-01|7\/1\/2026/)).toBeInTheDocument();

    // 关键守卫：全部字段都有数据，不应残留占位符 '-'
    expect(screen.queryByText('-')).toBeNull();
  });
});
