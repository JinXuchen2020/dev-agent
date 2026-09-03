import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import ReplayPanel from '../ReplayPanel';
import type { ReplayReport } from '../../types';

const report = (overrides: Partial<ReplayReport> = {}): ReplayReport => ({
  executionLogId: 'log-1',
  workflowId: 'wf-1',
  workflowName: 'failing-wf',
  overallStatus: 4, // Failed
  startedAt: '2026-09-01T00:00:00Z',
  completedAt: '2026-09-01T00:01:00Z',
  totalSteps: 3,
  nodes: [
    {
      stepOrder: 0, stepName: 'Generate Step', status: 3, nodeType: 2, isFailure: false,
      startedAt: '2026-09-01T00:00:00Z', completedAt: '2026-09-01T00:00:01Z', durationMs: 1200,
      input: null, inputInferred: true, output: 'draft output', outputLength: 12,
      outputTruncated: false, errorDetail: null, errorTruncated: false,
      tokensIn: 220, tokensOut: 96, tokensReported: true,
    },
    {
      stepOrder: 1, stepName: 'Review Step', status: 4, nodeType: 4, isFailure: true,
      startedAt: '2026-09-01T00:00:01Z', completedAt: '2026-09-01T00:00:02Z', durationMs: 80,
      input: 'draft output', inputInferred: true, output: null, outputLength: 0,
      outputTruncated: false, errorDetail: '模型返回超限', errorTruncated: false,
      tokensIn: 0, tokensOut: 0, tokensReported: false,
    },
  ],
  failurePath: { firstFailedStepOrder: 1, failedStepNames: ['Review Step'], failedCount: 1 },
  contextSnapshot: {
    available: true, source: 'F30-final-checkpoint', variables: { 'loop.x': '1' },
    checkpointVersion: 2, executionOrderIndex: 1, stepStateCount: 0,
    note: '末次检查点快照（F30 覆盖写，非 per-step 历史）',
  },
  recordedStepCount: 2,
  missingStepCount: 1,
  dataGaps: ['input-snapshot-unavailable', 'steps-missing-truncated-execution'],
  ...overrides,
});

const baseProps = { loading: false, error: null, onRetry: () => undefined };

describe('ReplayPanel（F40 回放诊断）', () => {
  it('失败路径：显示失败节点数与首个失败序号', () => {
    render(<ReplayPanel report={report()} {...baseProps} />);
    expect(screen.getByText(/发现 1 个失败节点/)).toBeInTheDocument();
    expect(screen.getByText('Review Step')).toBeInTheDocument();
    expect(screen.getByText('执行路径')).toBeInTheDocument();
  });

  it('成功执行：明确报「未发现失败节点」，不与「数据缺失」混淆', () => {
    const base = report();
    const greenNode = {
      ...base.nodes[1],
      status: 3,
      isFailure: false,
      errorDetail: null,
      output: 'reviewed',
      outputLength: 8,
    };
    const clean = report({
      overallStatus: 3,
      nodes: [base.nodes[0], greenNode],
      failurePath: { firstFailedStepOrder: null, failedStepNames: [], failedCount: 0 },
      missingStepCount: 0,
      dataGaps: ['input-snapshot-unavailable'],
    });
    render(<ReplayPanel report={clean} {...baseProps} />);
    expect(screen.getByText(/未发现失败节点/)).toBeInTheDocument();
  });

  it('无失败但含非完成态（暂停/回滚）：不得渲染「均为成功态」的假健康文案', () => {
    const mixed = report({
      overallStatus: 5, // RolledBack
      failurePath: { firstFailedStepOrder: null, failedStepNames: [], failedCount: 0 },
      nodes: [
        { ...report().nodes[0], status: 3 },
        { ...report().nodes[1], status: 2, isFailure: false, errorDetail: null }, // Paused 节点
      ],
    });
    render(<ReplayPanel report={mixed} {...baseProps} />);
    expect(screen.queryByText(/均为成功态/)).not.toBeInTheDocument();
    expect(screen.getByText(/非完成态/)).toBeInTheDocument();
  });

  it('数据缺口必须显式披露（含真实入参未记录）', () => {
    render(<ReplayPanel report={report()} {...baseProps} />);
    expect(screen.getByText(/数据缺口/)).toBeInTheDocument();
    expect(screen.getByText(/真实入参未落库/)).toBeInTheDocument();
    expect(screen.getByText(/日志条目少于登记步骤数/)).toBeInTheDocument();
  });

  it('推断输入需带标注，不冒充真实入参', () => {
    const { container } = render(<ReplayPanel report={report()} {...baseProps} />);
    const header = container.querySelector('.ant-collapse-header');
    expect(header).not.toBeNull();
    fireEvent.click(header!);
    expect(screen.getByText(/推断值/)).toBeInTheDocument();
  });

  it('上下文快照：展示变量并转述「仅末次快照」边界说明', () => {
    render(<ReplayPanel report={report()} {...baseProps} />);
    expect(screen.getByText('loop.x', { exact: false })).toBeInTheDocument();
    // 「末次检查点」同时出现在区块标题与快照边界说明里 —— 断言至少都存在，不锁定单一位置。
    expect(screen.getAllByText(/末次检查点/).length).toBeGreaterThanOrEqual(2);
  });

  it('快照不可用时降级提示，不抛错', () => {
    const noCtx = report({
      contextSnapshot: {
        available: false, source: null, variables: {}, checkpointVersion: null,
        executionOrderIndex: null, stepStateCount: 0, note: '无检查点数据',
      },
      dataGaps: ['context-snapshot-unavailable'],
    });
    render(<ReplayPanel report={noCtx} {...baseProps} />);
    expect(screen.getByText('无检查点数据')).toBeInTheDocument();
  });

  it('加载失败：给出可重试入口', () => {
    const onRetry = vi.fn();
    render(<ReplayPanel report={null} loading={false} error="boom" onRetry={onRetry} />);
    expect(screen.getByText(/回放诊断加载失败/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /重试/ }));
    expect(onRetry).toHaveBeenCalled();
  });
});
