import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  Typography,
  Tag,
  Button,
  Select,
  Input,
  Space,
  Alert,
  Spin,
  App as AntApp,
  Empty,
  Modal,
  Table,
} from 'antd';
import type { Agent, AgenticStreamEvent } from '../types';
import {
  getAgents,
  runAgentGoalStream,
  getAgentRunArtifacts,
  getAgentRunArtifactUrl,
  fetchAgentRunHistory,
  type AgentRunHistoryItem,
} from '../services/api';
import { useAppStore } from '../stores/appStore';
import { useTranslation } from 'react-i18next';
import { ArrowLeftOutlined } from '@ant-design/icons';
import Card from '../components/Card';
import { colors } from '../theme/tokens';

const { Title, Paragraph } = Typography;

const formatDuration = (ms: number): string => {
  if (ms < 1000) return `${ms}ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)}s`;
  const m = Math.floor(s / 60);
  const rem = Math.round(s % 60);
  return `${m}m${rem}s`;
};

const formatUtc = (iso: string): string => {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleString();
};

// 思考过程：工具调用 / 工具结果逐步累积
interface RunStep {
  iteration: number;
  toolName?: string;
  argumentsJson?: string;
  output?: string;
  success?: boolean;
  pending?: boolean;
}

const AgentRunPage: React.FC = () => {
  const { t } = useTranslation();
  const { message } = AntApp.useApp();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [agents, setAgents] = useState<Agent[]>([]);
  const [agentsLoading, setAgentsLoading] = useState(true);
  // 当前选中的 agent（优先读 URL ?agentId=，否则空）。
  const [selectedId, setSelectedId] = useState<string | null>(searchParams.get('agentId'));
  const [runGoal, setRunGoal] = useState('');
  const [runLoading, setRunLoading] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [runSteps, setRunSteps] = useState<RunStep[]>([]);
  const [runAnswer, setRunAnswer] = useState('');
  const [runSummary, setRunSummary] = useState<{ iterations: number; tokensIn: number; tokensOut: number } | null>(null);
  // 本次 run 的 id（后端在 done 事件未直接回传，故前端在发起时本地生成并随请求下发——这里改为从 done 事件透传）。
  const [runId, setRunId] = useState<string | null>(null);
  // 产物清单（来自 done 事件或完成后拉取）。
  const [runArtifacts, setRunArtifacts] = useState<{ path: string; size: number; contentType?: string }[]>([]);
  // 当前在 iframe 中预览的产物路径（仅 HTML 可预览）。
  const [previewPath, setPreviewPath] = useState<string | null>(null);
  // 运行历史（按 agent 查询，落库记录）。
  const [history, setHistory] = useState<AgentRunHistoryItem[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const runAbortRef = useRef<AbortController | null>(null);

  // RBAC: 后端 POST /agents/{id}/runs 仅允许 Admin,Operator。
  const userRole = useAppStore((s) => s.userRole);
  const canRun = !!userRole && ['admin', 'operator'].includes(userRole.toLowerCase());

  // 拉取 agent 列表用于下拉选择。
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await getAgents();
        if (cancelled) return;
        setAgents(list);
        // URL 未指定 agentId 但有列表时，默认选第一个。
        setSelectedId((prev) => prev ?? (list.length ? list[0].id : null));
      } catch (e: unknown) {
        if (!cancelled) message.error(t('pages.agents.loadFailed') + '：' + ((e as { message?: string }).message ?? ''));
      } finally {
        if (!cancelled) setAgentsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [message, t]);

  const selectedAgent = useMemo(
    () => agents.find((a) => a.id === selectedId) ?? null,
    [agents, selectedId],
  );

  // agent 切换时加载其运行历史。
  useEffect(() => {
    if (selectedId) {
      void loadHistory(selectedId);
    } else {
      setHistory([]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId]);

  const resetRun = () => {
    setRunGoal('');
    setRunError(null);
    setRunSteps([]);
    setRunAnswer('');
    setRunSummary(null);
    setRunId(null);
    setRunArtifacts([]);
    setPreviewPath(null);
  };

  const loadHistory = async (agentId: string) => {
    if (!agentId) return;
    setHistoryLoading(true);
    try {
      const list = await fetchAgentRunHistory(agentId);
      setHistory(list);
    } catch {
      /* 历史加载失败不阻断运行 */
    } finally {
      setHistoryLoading(false);
    }
  };

  // 选中一个历史 run：复用当前 run 的产物视图展示其结果与产物。
  const viewHistoryRun = async (item: AgentRunHistoryItem) => {
    if (!selectedAgent) return;
    setRunId(item.runId);
    setRunAnswer(item.finalAnswer ?? '');
    setRunSummary({
      iterations: item.iterations,
      tokensIn: item.totalTokensIn,
      tokensOut: item.totalTokensOut,
    });
    setRunError(item.status === 'Failed' ? item.errorMessage ?? t('pages.agents.runFailed') : null);
    // 拉取该 run 的产物清单。
    setRunArtifacts([]);
    try {
      const list = await getAgentRunArtifacts(selectedAgent.id, item.runId);
      setRunArtifacts(list);
    } catch {
      /* 无产物或拉取失败 */
    }
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleRun = () => {
    if (!selectedAgent || !runGoal.trim()) return;
    setRunLoading(true);
    setRunError(null);
    setRunSteps([]);
    setRunAnswer('');
    setRunSummary(null);
    setRunId(null);
    setRunArtifacts([]);
    setPreviewPath(null);

    const controller = new AbortController();
    runAbortRef.current = controller;

    runAgentGoalStream(selectedAgent.id, runGoal.trim(), (ev: AgenticStreamEvent) => {
      switch (ev.type) {
        case 'run_start':
          if (ev.runId) setRunId(ev.runId);
          break;
        case 'tool_call':
          setRunSteps((prev) => [
            ...prev,
            { iteration: ev.iteration ?? 0, toolName: ev.toolName, argumentsJson: ev.argumentsJson, pending: true },
          ]);
          break;
        case 'tool_result':
          setRunSteps((prev) => {
            const next = [...prev];
            for (let i = next.length - 1; i >= 0; i--) {
              if (next[i].pending) {
                next[i] = { ...next[i], output: ev.output, success: ev.success, pending: false };
                break;
              }
            }
            return next;
          });
          break;
        case 'answer_delta':
          setRunAnswer((prev) => prev + (ev.delta ?? ''));
          break;
        case 'done':
          setRunSummary({ iterations: ev.iteration ?? 0, tokensIn: ev.tokensIn ?? 0, tokensOut: ev.tokensOut ?? 0 });
          if (ev.finalAnswer != null) setRunAnswer(ev.finalAnswer);
          if (ev.artifacts && ev.artifacts.length) setRunArtifacts(ev.artifacts);
          break;
        case 'error':
          setRunError(ev.error ?? t('pages.agents.runFailed'));
          break;
        default:
          break;
      }
    }, controller.signal)
      .then(async () => {
        setRunLoading(false);
        // 完成后用 runId 拉取完整产物清单（done 事件内的清单可能不完整，以接口为准）。
        if (runId) {
          try {
            const list = await getAgentRunArtifacts(selectedAgent.id, runId);
            if (list.length) setRunArtifacts(list);
          } catch {
            /* 产物拉取失败不阻断结果展示 */
          }
        }
        // 注意：消息在流式结束后再提示，避免在 runError 被后续 done 覆盖前误报成功。
        if (!runError) message.success(t('pages.agents.runSuccess'));
      })
      .catch((e: unknown) => {
        if ((e as { name?: string })?.name === 'AbortError') return; // 用户取消
        setRunError((e as { message?: string }).message ?? t('pages.agents.runFailed'));
        setRunLoading(false);
      });
  };

  // 卸载时中断未完成的流，避免泄漏。
  useEffect(() => {
    return () => {
      runAbortRef.current?.abort();
    };
  }, []);

  if (!canRun) {
    return (
      <Card>
        <Empty description={t('pages.agents.permissionHint')} />
      </Card>
    );
  }

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/agents')}>
          {t('pages.agents.runBack')}
        </Button>
        <Title level={4} style={{ margin: 0 }}>
          {t('pages.agents.runAgent')}
          {selectedAgent ? ` — ${selectedAgent.name}` : ''}
        </Title>
      </div>

      <Card>
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <div>
            <Paragraph style={{ marginBottom: 6, fontWeight: 600 }}>{t('pages.agents.selectAgent')}</Paragraph>
            {agentsLoading ? (
              <Spin />
            ) : (
              <Select
                style={{ width: '100%', maxWidth: 520 }}
                value={selectedId ?? undefined}
                onChange={(id) => {
                  setSelectedId(id);
                  resetRun();
                }}
                placeholder={t('pages.agents.selectAgentPlaceholder')}
                options={agents.map((a) => ({ label: `${a.name}（${a.roleCode ?? '-'}）`, value: a.id }))}
              />
            )}
          </div>

          <Input.TextArea
            rows={4}
            value={runGoal}
            onChange={(e) => setRunGoal(e.target.value)}
            placeholder={t('pages.agents.runGoalPlaceholder')}
            disabled={!selectedAgent || runLoading}
          />

          <Space style={{ alignSelf: 'flex-start' }}>
            <Button
              type="primary"
              loading={runLoading}
              disabled={!selectedAgent || !runGoal.trim() || runLoading}
              onClick={handleRun}
            >
              {t('pages.agents.runExecute')}
            </Button>
            {runLoading && (
              <Button danger onClick={() => runAbortRef.current?.abort()}>
                {t('pages.agents.runStop')}
              </Button>
            )}
          </Space>

          {runError && <Alert type="error" showIcon message={runError} />}

          {runLoading && runSteps.length === 0 && !runAnswer && (
            <span style={{ color: colors.textMuted, fontSize: 13 }}>{t('pages.agents.runThinking')}</span>
          )}

          {(runSteps.length > 0 || runAnswer || runSummary) && (
            <div>
              {runSteps.length > 0 && (
                <>
                  <Paragraph style={{ marginTop: 8, fontWeight: 600 }}>{t('pages.agents.runTrace')}</Paragraph>
                  <Space direction="vertical" size={4} style={{ width: '100%' }}>
                    {runSteps.map((s, i) => (
                      <div key={i} style={{ fontSize: 13 }}>
                        <Tag color={s.pending ? 'gold' : s.success ? 'green' : 'red'}>#{s.iteration}</Tag>
                        {s.toolName && <Tag color="blue">{s.toolName}</Tag>}
                        {s.pending ? (
                          <span style={{ color: colors.textMuted }}>执行中…</span>
                        ) : (
                          <span style={{ color: colors.textMuted }}>{s.output}</span>
                        )}
                      </div>
                    ))}
                  </Space>
                </>
              )}
              <Paragraph style={{ marginTop: 12, fontWeight: 600 }}>{t('pages.agents.runFinalAnswer')}</Paragraph>
              <Paragraph style={{ whiteSpace: 'pre-wrap', background: colors.surfaceMuted, padding: 12, borderRadius: 8 }}>
                {runAnswer}
                {runLoading && <span style={{ color: colors.textMuted }}>▍</span>}
              </Paragraph>
              {runSummary && (
                <Alert
                  type="success"
                  showIcon
                  message={t('pages.agents.runResult')}
                  description={`${t('pages.agents.runIterations')}: ${runSummary.iterations} · Tokens: ${runSummary.tokensIn}/${runSummary.tokensOut}`}
                />
              )}

              {runArtifacts.length > 0 && runId && (
                <div>
                  <Paragraph style={{ marginTop: 12, fontWeight: 600 }}>{t('pages.agents.runArtifacts')}</Paragraph>
                  <Space direction="vertical" size={6} style={{ width: '100%' }}>
                    {runArtifacts.map((a) => (
                      <div key={a.path} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                        <Tag color="geekblue">{a.path}</Tag>
                        <span style={{ color: colors.textMuted }}>{(a.size / 1024).toFixed(1)} KB</span>
                        {a.contentType === 'text/html' ? (
                          <Button size="small" type="link" onClick={() => setPreviewPath(a.path)}>
                            {t('pages.agents.runPreview')}
                          </Button>
                        ) : (
                          <a
                            href={getAgentRunArtifactUrl(selectedAgent!.id, runId, a.path)}
                            target="_blank"
                            rel="noreferrer"
                            style={{ fontSize: 13 }}
                          >
                            {t('pages.agents.runDownload')}
                          </a>
                        )}
                      </div>
                    ))}
                  </Space>
                </div>
              )}
            </div>
          )}
        </Space>
      </Card>

      <Card style={{ marginTop: 16 }}>
        <Paragraph style={{ marginBottom: 12, fontWeight: 600 }}>
          {t('pages.agents.runHistory')}
          {historyLoading && <Spin size="small" style={{ marginLeft: 8 }} />}
        </Paragraph>
        {history.length === 0 && !historyLoading ? (
          <Empty description={t('pages.agents.runHistoryEmpty')} />
        ) : (
          <Table<AgentRunHistoryItem>
            size="small"
            pagination={false}
            dataSource={history}
            rowKey={(r) => r.runId}
            columns={[
              {
                title: t('pages.agents.runHistoryGoal'),
                dataIndex: 'goal',
                ellipsis: true,
                render: (goal: string) => <span style={{ fontSize: 13 }}>{goal}</span>,
              },
              {
                title: t('pages.agents.runHistoryStatus'),
                dataIndex: 'status',
                width: 110,
                render: (s: AgentRunHistoryItem['status']) => (
                  <Tag color={s === 'Completed' ? 'green' : s === 'Failed' ? 'red' : 'gold'}>
                    {t(`pages.agents.runStatus_${s.toLowerCase()}`)}
                  </Tag>
                ),
              },
              {
                title: t('pages.agents.runHistoryIter'),
                dataIndex: 'iterations',
                width: 80,
                render: (n: number) => <span style={{ fontSize: 13 }}>{n}</span>,
              },
              {
                title: t('pages.agents.runHistoryDuration'),
                dataIndex: 'durationMs',
                width: 110,
                render: (ms: number) => <span style={{ fontSize: 13 }}>{formatDuration(ms)}</span>,
              },
              {
                title: t('pages.agents.runHistoryArtifacts'),
                dataIndex: 'artifactCount',
                width: 90,
                render: (n: number) => <span style={{ fontSize: 13 }}>{n}</span>,
              },
              {
                title: t('pages.agents.runHistoryTime'),
                dataIndex: 'createdAt',
                width: 170,
                render: (d: string) => <span style={{ fontSize: 13, color: colors.textMuted }}>{formatUtc(d)}</span>,
              },
              {
                title: '',
                width: 90,
                render: (_: unknown, item: AgentRunHistoryItem) => (
                  <Button size="small" type="link" onClick={() => void viewHistoryRun(item)}>
                    {t('pages.agents.runHistoryView')}
                  </Button>
                ),
              },
            ]}
          />
        )}
      </Card>

      <Modal
        title={previewPath ?? t('pages.agents.runPreview')}
        open={!!previewPath}
        onCancel={() => setPreviewPath(null)}
        footer={null}
        width="80%"
        style={{ top: 24 }}
      >
        {previewPath && runId && selectedAgent && (
          <iframe
            src={getAgentRunArtifactUrl(selectedAgent.id, runId, previewPath)}
            title={previewPath}
            style={{ width: '100%', height: '75vh', border: 'none', background: '#fff' }}
          />
        )}
      </Modal>
    </div>
  );
};

export default AgentRunPage;
