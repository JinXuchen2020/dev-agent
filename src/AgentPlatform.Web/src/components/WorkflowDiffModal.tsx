import React from 'react';
import { Modal, Spin, Empty, Typography, Tag, Descriptions, Card, Space, Collapse, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { WorkflowDiffDto, WorkflowDiffNode } from '../types';
import { StepType } from '../types';

const { Text, Paragraph } = Typography;

// StepType 数值 → canvas.nodeType 的 i18n key（与画布节点类型标签一致）。
const NODE_TYPE_I18N_KEY: Record<number, string> = {
  [StepType.Start]: 'start',
  [StepType.End]: 'end',
  [StepType.LLM]: 'llm',
  [StepType.Agent]: 'agent',
  [StepType.Critic]: 'critic',
  [StepType.Knowledge]: 'knowledge',
  [StepType.Tool]: 'tool',
  [StepType.Code]: 'code',
  [StepType.Http]: 'http',
  [StepType.Condition]: 'condition',
  [StepType.Loop]: 'loop',
  [StepType.Variable]: 'variable',
  [StepType.SubWorkflow]: 'subWorkflow',
  [StepType.Delay]: 'delay',
  [StepType.UserInput]: 'userInput',
};

function nodeTypeLabel(t: (k: string) => string, type: number): string {
  const key = NODE_TYPE_I18N_KEY[type] ?? 'llm';
  return t(`pages.workflows.canvas.nodeType.${key}`);
}

function prettyConfig(configJson: string | null): string {
  if (!configJson) return '—';
  try {
    return JSON.stringify(JSON.parse(configJson), null, 2);
  } catch {
    return configJson;
  }
}

function NodeCard({
  t,
  node,
  tone,
}: {
  t: (k: string) => string;
  node: WorkflowDiffNode;
  tone: 'added' | 'removed';
}): React.ReactElement {
  const color = tone === 'added' ? 'green' : 'red';
  return (
    <Card size="small" style={{ marginBottom: 8 }} title={null}>
      <Space direction="vertical" size={4} style={{ width: '100%' }}>
        <Space>
          <Tag color={color}>{node.name}</Tag>
          <Tag>{nodeTypeLabel(t, node.type)}</Tag>
        </Space>
        <Paragraph style={{ margin: 0 }}>
          <Text type="secondary">{t('pages.workflows.diff.config')}：</Text>
        </Paragraph>
        <pre
          style={{
            background: '#fafafa',
            border: '1px solid #f0f0f0',
            borderRadius: 6,
            padding: 8,
            fontSize: 12,
            maxHeight: 180,
            overflow: 'auto',
            margin: 0,
          }}
        >
          {prettyConfig(node.configJson)}
        </pre>
      </Space>
    </Card>
  );
}

interface Props {
  open: boolean;
  loading: boolean;
  data: WorkflowDiffDto | null;
  onClose: () => void;
}

const WorkflowDiffModal: React.FC<Props> = ({ open, loading, data, onClose }) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const addedCount = data?.addedNodes.length ?? 0;
  const removedCount = data?.removedNodes.length ?? 0;
  const changedCount = data?.changedNodes.length ?? 0;
  const addedEdgeCount = data?.addedEdges.length ?? 0;
  const removedEdgeCount = data?.removedEdges.length ?? 0;
  const contextChanged = data?.contextChanged ?? false;

  const hasChange =
    addedCount + removedCount + changedCount + addedEdgeCount + removedEdgeCount > 0 ||
    contextChanged;

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      width={720}
      title={data ? t('pages.workflows.diff.title', { from: data.fromLabel, to: data.toLabel }) : t('pages.workflows.diff.action')}
    >
      {loading ? (
        <div style={{ textAlign: 'center', padding: 48 }}>
          <Spin />
        </div>
      ) : !data ? (
        <Empty description={t('pages.usage.empty')} />
      ) : !hasChange ? (
        <Empty description={t('pages.workflows.diff.noChange')} />
      ) : (
        <div style={{ maxHeight: '70vh', overflow: 'auto' }}>
          {/* 概览 */}
          <Paragraph style={{ marginBottom: 12 }}>
            <Space wrap>
              <Tag color="green">{t('pages.workflows.diff.addedNodes')}: {addedCount}</Tag>
              <Tag color="red">{t('pages.workflows.diff.removedNodes')}: {removedCount}</Tag>
              <Tag color="orange">{t('pages.workflows.diff.changedNodes')}: {changedCount}</Tag>
              <Tag color="green">{t('pages.workflows.diff.addedEdges')}: {addedEdgeCount}</Tag>
              <Tag color="red">{t('pages.workflows.diff.removedEdges')}: {removedEdgeCount}</Tag>
            </Space>
          </Paragraph>

          {/* 上下文变更 */}
          {contextChanged && (
            <Card
              size="small"
              style={{ marginBottom: 12, borderColor: token.colorWarning }}
              title={<Tag color="orange">{t('pages.workflows.diff.contextChanged')}</Tag>}
            >
              <Descriptions column={1} size="small" bordered>
                <Descriptions.Item label={t('pages.workflows.diff.before')}>
                  <pre style={{ margin: 0, fontSize: 12 }}>{data.contextBefore ?? '—'}</pre>
                </Descriptions.Item>
                <Descriptions.Item label={t('pages.workflows.diff.after')}>
                  <pre style={{ margin: 0, fontSize: 12 }}>{data.contextAfter ?? '—'}</pre>
                </Descriptions.Item>
              </Descriptions>
            </Card>
          )}

          {/* 新增节点 */}
          {addedCount > 0 && (
            <Collapse
              size="small"
              defaultActiveKey={['added']}
              style={{ marginBottom: 12 }}
              items={[
                {
                  key: 'added',
                  label: (
                    <Text strong style={{ color: '#3f8600' }}>
                      {t('pages.workflows.diff.addedNodes')} ({addedCount})
                    </Text>
                  ),
                  children: (
                    <div>
                      {data!.addedNodes.map((n) => (
                        <NodeCard key={n.id} t={t} node={n} tone="added" />
                      ))}
                    </div>
                  ),
                },
              ]}
            />
          )}

          {/* 删除节点 */}
          {removedCount > 0 && (
            <Collapse
              size="small"
              defaultActiveKey={['removed']}
              style={{ marginBottom: 12 }}
              items={[
                {
                  key: 'removed',
                  label: (
                    <Text strong style={{ color: '#cf1322' }}>
                      {t('pages.workflows.diff.removedNodes')} ({removedCount})
                    </Text>
                  ),
                  children: (
                    <div>
                      {data!.removedNodes.map((n) => (
                        <NodeCard key={n.id} t={t} node={n} tone="removed" />
                      ))}
                    </div>
                  ),
                },
              ]}
            />
          )}

          {/* 变更节点 */}
          {changedCount > 0 && (
            <Collapse
              size="small"
              defaultActiveKey={['changed']}
              style={{ marginBottom: 12 }}
              items={[
                {
                  key: 'changed',
                  label: (
                    <Text strong style={{ color: '#d46b08' }}>
                      {t('pages.workflows.diff.changedNodes')} ({changedCount})
                    </Text>
                  ),
                  children: (
                    <div>
                      {data!.changedNodes.map((c) => (
                        <Card key={c.id} size="small" style={{ marginBottom: 8 }}>
                          <Space style={{ marginBottom: 8 }}>
                            <Tag color="orange">{c.after.name}</Tag>
                            <Tag>{nodeTypeLabel(t, c.after.type)}</Tag>
                          </Space>
                          <Descriptions column={2} size="small" bordered>
                            <Descriptions.Item label={t('pages.workflows.diff.before')}>
                              <pre
                                style={{
                                  margin: 0,
                                  fontSize: 12,
                                  maxHeight: 160,
                                  overflow: 'auto',
                                  background: '#fff1f0',
                                  padding: 6,
                                  borderRadius: 4,
                                }}
                              >
                                {prettyConfig(c.before.configJson)}
                              </pre>
                            </Descriptions.Item>
                            <Descriptions.Item label={t('pages.workflows.diff.after')}>
                              <pre
                                style={{
                                  margin: 0,
                                  fontSize: 12,
                                  maxHeight: 160,
                                  overflow: 'auto',
                                  background: '#f6ffed',
                                  padding: 6,
                                  borderRadius: 4,
                                }}
                              >
                                {prettyConfig(c.after.configJson)}
                              </pre>
                            </Descriptions.Item>
                          </Descriptions>
                        </Card>
                      ))}
                    </div>
                  ),
                },
              ]}
            />
          )}

          {/* 边变更 */}
          {(addedEdgeCount + removedEdgeCount) > 0 && (
            <Collapse
              size="small"
              defaultActiveKey={['edges']}
              items={[
                {
                  key: 'edges',
                  label: <Text strong>{t('pages.workflows.diff.edgesSection')}</Text>,
                  children: (
                    <Space direction="vertical" size={8} style={{ width: '100%' }}>
                      {data!.addedEdges.map((e, i) => (
                        <Text key={`ae-${i}`}>
                          <Tag color="green">+</Tag>
                          {e.sourceName} → {e.targetName}
                          {e.label ? `（${t('pages.workflows.diff.edgeLabel')}: ${e.label}）` : ''}
                        </Text>
                      ))}
                      {data!.removedEdges.map((e, i) => (
                        <Text key={`re-${i}`} type="danger">
                          <Tag color="red">−</Tag>
                          {e.sourceName} → {e.targetName}
                          {e.label ? `（${t('pages.workflows.diff.edgeLabel')}: ${e.label}）` : ''}
                        </Text>
                      ))}
                    </Space>
                  ),
                },
              ]}
            />
          )}
        </div>
      )}
    </Modal>
  );
};

export default WorkflowDiffModal;
