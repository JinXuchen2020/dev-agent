import { useEffect, useState } from 'react';
import { Input, Select, Button, Typography, Form } from 'antd';
import { DeleteOutlined } from '@ant-design/icons';
import { useCanvasStore, STEP_TYPE_LABEL } from '../../stores/workflowCanvasStore';
import { getAgents, getKnowledgeBases } from '../../services/api';
import { StepType } from '../../types';
import type { Agent, KnowledgeBase } from '../../types';

const panelStyle: React.CSSProperties = {
  width: 300,
  padding: 16,
  borderLeft: '1px solid #f0f0f0',
  background: '#fff',
  overflowY: 'auto',
};

export default function NodeConfigPanel() {
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
        <Typography.Text type="secondary">选择一个节点以编辑配置</Typography.Text>
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
        {STEP_TYPE_LABEL[type]} 配置
      </Typography.Text>
      <Form layout="vertical" style={{ marginTop: 12 }}>
        <Form.Item label="节点名称">
          <Input
            value={label}
            onFocus={snapshot}
            onChange={(e) => patch({ label: e.target.value })}
          />
        </Form.Item>

        {type === StepType.Start && (
          <Form.Item label="入口上下文 (initialContext JSON)" tooltip="工作流级别的初始上下文">
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
          <Form.Item label="System Prompt 模板" tooltip="可插入 {{artifacts}} 占位符">
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
          <Form.Item label="分配 Agent">
            <Select
              allowClear
              placeholder="选择 Agent"
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
          <Form.Item label="评审标准">
            <Input.TextArea
              rows={4}
              value={config?.criteria ?? ''}
              onFocus={snapshot}
              onChange={(e) => patchConfig({ criteria: e.target.value })}
              placeholder="检查输出是否满足要求，给出通过/不通过理由"
            />
          </Form.Item>
        )}

        {type === StepType.Knowledge && (
          <>
            <Form.Item label="知识库" tooltip="从该知识库的向量集合检索">
              <Select
                allowClear
                placeholder="选择知识库"
                value={config?.knowledgeBaseId ?? undefined}
                onFocus={snapshot}
                onChange={(value) => patchConfig({ knowledgeBaseId: value ?? null })}
                options={knowledgeBases.map((kb) => ({ value: kb.id, label: kb.name }))}
              />
            </Form.Item>
            <Form.Item label="查询（可选）" tooltip="留空则使用上游节点输出作为查询">
              <Input.TextArea
                rows={3}
                value={config?.query ?? ''}
                onFocus={snapshot}
                onChange={(e) => patchConfig({ query: e.target.value })}
                placeholder="例如：产品退货政策"
              />
            </Form.Item>
          </>
        )}

        {type === StepType.End && (
          <Form.Item label="汇总方式" tooltip="默认拼接所有前驱 artifacts">
            <Input
              value={config?.summary ?? 'all'}
              onFocus={snapshot}
              onChange={(e) => patchConfig({ summary: e.target.value })}
            />
          </Form.Item>
        )}

        {(node.data.state || node.data.result) && (
          <Form.Item label="运行时状态">
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
        删除节点
      </Button>
    </div>
  );
}
