import { useEffect, useState } from 'react';
import { Input, Select, Button, Typography, Form } from 'antd';
import { DeleteOutlined } from '@ant-design/icons';
import { useCanvasStore } from '../../stores/workflowCanvasStore';
import { getAgents, getKnowledgeBases } from '../../services/api';
import { StepType } from '../../types';
import type { Agent, KnowledgeBase } from '../../types';
import { useTranslation } from 'react-i18next';

const panelStyle: React.CSSProperties = {
  width: 300,
  padding: 16,
  borderLeft: '1px solid #f0f0f0',
  background: '#fff',
  overflowY: 'auto',
};

export default function NodeConfigPanel() {
  const { t } = useTranslation();
  const NODE_TYPE_LABEL: Record<StepType, string> = {
    [StepType.Start]: t('canvas.nodeType.start'),
    [StepType.End]: t('canvas.nodeType.end'),
    [StepType.LLM]: t('canvas.nodeType.llm'),
    [StepType.Agent]: t('canvas.nodeType.agent'),
    [StepType.Critic]: t('canvas.nodeType.critic'),
    [StepType.Knowledge]: t('canvas.nodeType.knowledge'),
    [StepType.Tool]: t('canvas.nodeType.tool'),
    [StepType.Code]: t('canvas.nodeType.code'),
  };
  const node = useCanvasStore((s) => s.nodes.find((n) => n.id === s.selectedNodeId));
  const setNodeData = useCanvasStore((s) => s.setNodeData);
  const removeNode = useCanvasStore((s) => s.removeNode);
  const snapshot = useCanvasStore((s) => s.snapshot);
  const initialContext = useCanvasStore((s) => s.initialContext);
  const setInitialContext = useCanvasStore((s) => s.setInitialContext);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);

  const stepType = node?.data.stepType;
  useEffect(() => {
    if (stepType === StepType.Agent) {
      getAgents()
        .then(setAgents)
        .catch(() => setAgents([]));
    }
    if (stepType === StepType.Knowledge) {
      getKnowledgeBases()
        .then(setKnowledgeBases)
        .catch(() => setKnowledgeBases([]));
    }
  }, [stepType]);

  if (!node) {
    return (
      <div style={panelStyle}>
        <Typography.Text type="secondary">{t('canvas.selectNode')}</Typography.Text>
      </div>
    );
  }

  const { stepType: type, config, label } = node.data;

  const patch = (p: Parameters<typeof setNodeData>[1]) => setNodeData(node.id, p);
  const patchConfig = (cfgPatch: Record<string, unknown>) =>
    setNodeData(node.id, { config: { ...(config ?? {}), ...cfgPatch } });

  return (
    <div style={panelStyle}>
      <Typography.Text strong>
        {NODE_TYPE_LABEL[type]} {t('canvas.config')}
      </Typography.Text>
      <Form layout="vertical" style={{ marginTop: 12 }}>
        <Form.Item label={t('canvas.nodeName')}>
          <Input
            value={label}
            onFocus={snapshot}
            onChange={(e) => patch({ label: e.target.value })}
          />
        </Form.Item>

        {type === StepType.Start && (
          <Form.Item label={t('canvas.initialContext')} tooltip={t('canvas.initialContextTooltip')}>
            <Input.TextArea
              rows={4}
              value={initialContext}
              onFocus={snapshot}
              onChange={(e) => setInitialContext(e.target.value)}
              placeholder='{"topic":"..."}'
            />
          </Form.Item>
        )}

        {type === StepType.LLM && (
          <Form.Item label={t('canvas.systemPromptTemplate')} tooltip={t('canvas.systemPromptTooltip')}>
            <Input.TextArea
              rows={5}
              value={config?.systemPrompt ?? ''}
              onFocus={snapshot}
              onChange={(e) => patchConfig({ systemPrompt: e.target.value })}
              placeholder="你是一名助手，根据上下文完成：{{artifacts}}"
            />
          </Form.Item>
        )}

        {type === StepType.Agent && (
          <Form.Item label={t('canvas.assignAgent')}>
            <Select
              allowClear
              placeholder={t('canvas.agentPlaceholder')}
              value={config?.agentId ?? undefined}
              onFocus={snapshot}
              onChange={(value) =>
                setNodeData(node.id, {
                  assignedAgentId: value ?? null,
                  config: { ...(config ?? {}), agentId: value ?? null },
                })
              }
              options={agents.map((a) => ({ value: a.id, label: a.name }))}
            />
          </Form.Item>
        )}

        {type === StepType.Critic && (
          <Form.Item label={t('canvas.reviewCriteria')}>
            <Input.TextArea
              rows={4}
              value={config?.criteria ?? ''}
              onFocus={snapshot}
              onChange={(e) => patchConfig({ criteria: e.target.value })}
              placeholder={t('canvas.reviewPlaceholder')}
            />
          </Form.Item>
        )}

        {type === StepType.Knowledge && (
          <>
            <Form.Item label={t('canvas.knowledgeBase')} tooltip={t('canvas.kbTooltip')}>
              <Select
                allowClear
                placeholder={t('canvas.kbPlaceholder')}
                value={config?.knowledgeBaseId ?? undefined}
                onFocus={snapshot}
                onChange={(value) => patchConfig({ knowledgeBaseId: value ?? null })}
                options={knowledgeBases.map((kb) => ({ value: kb.id, label: kb.name }))}
              />
            </Form.Item>
            <Form.Item label={t('canvas.queryLabel')} tooltip={t('canvas.queryTooltip')}>
              <Input.TextArea
                rows={3}
                value={config?.query ?? ''}
                onFocus={snapshot}
                onChange={(e) => patchConfig({ query: e.target.value })}
                placeholder={t('canvas.kbQueryPlaceholder')}
              />
            </Form.Item>
          </>
        )}

        {type === StepType.Tool && (
          <>
            <Form.Item
              label={t('canvas.toolName')}
              tooltip={t('canvas.toolNameTooltip')}
            >
              <Input
                value={config?.toolName ?? ''}
                onFocus={snapshot}
                onChange={(e) => patchConfig({ toolName: e.target.value })}
                placeholder="例如：web_search"
              />
            </Form.Item>
            <Form.Item
              label={t('canvas.paramsLabel')}
              tooltip={t('canvas.paramsTooltip')}
            >
              <Input.TextArea
                rows={5}
                value={config?.parameters ?? ''}
                onFocus={snapshot}
                onChange={(e) => patchConfig({ parameters: e.target.value })}
                placeholder={'{\n  "query": "{{artifacts}}"\n}'}
              />
            </Form.Item>
          </>
        )}

        {type === StepType.Code && (
          <>
            <Form.Item label={t('canvas.languageLabel')}>
              <Select
                value={config?.language ?? 'python'}
                onFocus={snapshot}
                onChange={(value) => patchConfig({ language: value })}
                options={[
                  { value: 'python', label: 'Python' },
                  { value: 'javascript', label: 'JavaScript' },
                  { value: 'csscript', label: 'C# Script' },
                ]}
              />
            </Form.Item>
            <Form.Item
              label={t('canvas.codeLabel')}
              tooltip={t('canvas.codeTooltip')}
            >
              <Input.TextArea
                rows={8}
                value={config?.code ?? ''}
                onFocus={snapshot}
                onChange={(e) => patchConfig({ code: e.target.value })}
                placeholder={'print("hello from sandbox")'}
              />
            </Form.Item>
          </>
        )}

        {type === StepType.End && (
          <Form.Item label={t('canvas.summaryLabel')} tooltip={t('canvas.summaryTooltip')}>
            <Input
              value={config?.summary ?? 'all'}
              onFocus={snapshot}
              onChange={(e) => patchConfig({ summary: e.target.value })}
            />
          </Form.Item>
        )}

        {(node.data.state || node.data.result) && (
          <Form.Item label={t('canvas.runtimeState')}>
            <Typography.Paragraph style={{ marginBottom: 4 }}>
              <b>{node.data.state ?? '—'}</b>
              {node.data.errorDetail ? (
                <span style={{ color: '#ff4d4f' }}> · {node.data.errorDetail}</span>
              ) : null}
            </Typography.Paragraph>
            {node.data.result ? (
              <Input.TextArea readOnly rows={4} value={node.data.result} />
            ) : null}
          </Form.Item>
        )}
      </Form>

      <Button danger block icon={<DeleteOutlined />} onClick={() => removeNode(node.id)}>
        {t('canvas.deleteNode')}
      </Button>
    </div>
  );
}
