import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import AgentRolesPage from '../pages/AgentRolesPage';
import * as api from '../services/api';
import type { AgentRole } from '../types';

// 回归测试：编辑角色时 Modal 表单必须回填该行数据。
// 历史 bug：antd Modal（rc-dialog）在首次打开前懒挂载内部 Form，首次点编辑时
// setFieldsValue 写进未连接的 Form 实例 → 字段全空（第二次才生效）。修复 =
// Modal 加 `forceRender` 让 Form 从页面加载起常驻挂载 + useEffect 回填。
// 点击编辑应立即回填 name/roleCode/description/systemPrompt。
const ROLE: AgentRole = {
  id: 'r1',
  name: '系统架构',
  roleCode: 'architecture',
  description: '设计系统架构',
  systemPrompt: '你负责架构设计',
  isBuiltIn: true,
  agentCount: 0,
};

vi.mock('../services/api', () => ({
  getAgentRoles: vi.fn(),
  createAgentRole: vi.fn(),
  updateAgentRole: vi.fn(),
  deleteAgentRole: vi.fn(),
}));

vi.mock('../stores/appStore', () => ({
  useAppStore: (selector: (s: unknown) => unknown) =>
    selector({ userRole: 'Admin', user: null, loginReal: vi.fn(), loginDemo: vi.fn() }),
}));

beforeEach(() => {
  vi.mocked(api.getAgentRoles).mockResolvedValue([ROLE]);
});

describe('AgentRolesPage 编辑回填', () => {
  it('点击编辑后四个表单字段应回填角色数据', async () => {
    const { container } = render(<AgentRolesPage />);

    // 等待列表加载完成（角色卡片出现）
    await waitFor(() => expect(screen.getByText('系统架构')).toBeInTheDocument());

    // 内置角色只有「编辑」图标按钮（无删除）；用图标 class 定位其祖先 button，
    // 避免依赖 antd 图标按钮的 accessible name（受 Tooltip/icon 影响不稳定）。
    const editBtn = container.querySelector('.anticon-edit')?.closest('button') as
      | HTMLElement
      | undefined;
    expect(editBtn).toBeTruthy();
    fireEvent.click(editBtn!);

    // Modal 打开后，表单必须回填（用 displayValue 锁定 input/textarea 的 value）
    await waitFor(() => {
      expect(screen.getByDisplayValue('系统架构')).toBeInTheDocument();
    });
    expect(screen.getByDisplayValue('architecture')).toBeInTheDocument();
    expect(screen.getByDisplayValue('设计系统架构')).toBeInTheDocument();
    expect(screen.getByDisplayValue('你负责架构设计')).toBeInTheDocument();
  });
});
