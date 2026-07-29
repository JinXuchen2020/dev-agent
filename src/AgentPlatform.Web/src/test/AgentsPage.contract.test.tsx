import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import AgentsPage from '../pages/AgentsPage';
import * as api from '../services/api';
import type { Agent } from '../types';

// 严格按照后端 AgentResponse 的真实形状构造（camelCase、扁平 roleCode / modelName /
// modelProvider / status "Active"/"Inactive" / systemPrompt / tenantId / createdAt）。
// 这一层就是“API 契约”。如果前端列映射错位（读成嵌套 role / modelEndpoint、
// 缺 systemPrompt），表格会渲染成 '-' 或空白，下面的断言就会失败。
const SAMPLE: Agent[] = [
  {
    id: 'a1',
    name: '文档摘要助手',
    roleCode: 'developer',
    modelProvider: 'openai',
    modelName: 'gpt-4o',
    tenantId: '00000000-0000-0000-0000-000000000001',
    status: 'Active',
    systemPrompt: '你是一个文档摘要助手',
    createdAt: '2026-07-01T10:00:00Z',
  },
];

vi.mock('../services/api', () => ({
  getAgents: vi.fn(),
  getAgentRoles: vi.fn(),
  getPlatformModels: vi.fn(),
  createAgent: vi.fn(),
  updateAgent: vi.fn(),
  deleteAgent: vi.fn(),
}));

beforeEach(() => {
  vi.mocked(api.getAgents).mockResolvedValue(SAMPLE);
  vi.mocked(api.getAgentRoles).mockResolvedValue([]);
  vi.mocked(api.getPlatformModels).mockResolvedValue([]);
});

describe('AgentsPage 字段映射契约', () => {
  it('把 API 返回的真实字段渲染进卡片网格（不出现占位符 "-" 或空白）', async () => {
    render(<AgentsPage />);

    // 等待列表异步加载完成
    await waitFor(() => expect(screen.getByText('文档摘要助手')).toBeInTheDocument());

    // 角色：卡片以「角色: developer」呈现，正则匹配子串而非曾经的 '-'
    expect(screen.getByText(/developer/)).toBeInTheDocument();

    // Model：卡片以「模型: gpt-4o」呈现，正则匹配子串而非曾经的 '-'
    expect(screen.getByText(/gpt-4o/)).toBeInTheDocument();

    // System Prompt：必须出现真实内容，而不是曾经的空白
    expect(screen.getByText(/你是一个文档摘要助手/)).toBeInTheDocument();

    // 状态：必须渲染出 status（徽章文字），而不是曾经的 undefined→空
    expect(screen.getByText('Active')).toBeInTheDocument();

    // 创建时间：必须渲染出时间，而不是曾经的空白
    expect(screen.getByText(/2026\/7\/1|2026-07-01|7\/1\/2026/)).toBeInTheDocument();

    // 关键守卫：全部字段都有数据，不应残留占位符 '-'
    expect(screen.queryByText('-')).toBeNull();
  });
});
