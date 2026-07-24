import React, { useState } from 'react';
import {
  Input,
  InputNumber,
  Button,
  Typography,
  Tag,
  Timeline,
  Alert,
  Empty,
  Space,
  Divider,
  List,
  App as AntApp,
} from 'antd';
import { SearchOutlined, StopOutlined } from '@ant-design/icons';
import { runResearch, getErrorMessage } from '../services/api';
import { ResearchEventTypeValue, type ResearchProgressEvent, type ResearchReport } from '../types';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';

const { Text, Paragraph, Title } = Typography;

const ResearchPage: React.FC = () => {
  const { message } = AntApp.useApp();
  const [question, setQuestion] = useState('');
  const [focus, setFocus] = useState('');
  const [maxSteps, setMaxSteps] = useState<number | null>(3);
  const [running, setRunning] = useState(false);
  const [events, setEvents] = useState<ResearchProgressEvent[]>([]);
  const [report, setReport] = useState<ResearchReport | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [abort, setAbort] = useState<AbortController | null>(null);

  const handleRun = async () => {
    const q = question.trim();
    if (!q || running) return;
    const controller = new AbortController();
    setAbort(controller);
    setRunning(true);
    setErrorMsg(null);
    setReport(null);
    setEvents([]);
    try {
      await runResearch(
        {
          question: q,
          maxSteps: maxSteps ?? null,
          focusInstructions: focus.trim() || null,
          modelId: null,
        },
        (e) => {
          setEvents((prev) => [...prev, e]);
          if (e.type === ResearchEventTypeValue.Report && e.report) setReport(e.report);
          if (e.type === ResearchEventTypeValue.Error) setErrorMsg(e.error ?? '未知错误');
        },
        controller.signal,
      );
    } catch (err) {
      const name = (err as { name?: string })?.name;
      if (name !== 'AbortError') {
        message.error('调研失败：' + getErrorMessage(err));
      }
    } finally {
      setRunning(false);
      setAbort(null);
    }
  };

  const handleStop = () => {
    abort?.abort();
  };

  const timelineItems = events
    .filter((e) => e.type !== ResearchEventTypeValue.Report)
    .map((e) => {
      switch (e.type) {
        case ResearchEventTypeValue.Plan:
          return {
            color: 'blue',
            children: (
              <div>
                <Text strong>已规划 {e.queries?.length ?? 0} 个检索查询</Text>
                <div style={{ marginTop: 6 }}>
                  {(e.queries ?? []).map((q, i) => (
                    <Tag key={i}>{q}</Tag>
                  ))}
                </div>
              </div>
            ),
          };
        case ResearchEventTypeValue.SearchStart:
          return { color: 'blue', children: <Text>检索中：{e.query}</Text> };
        case ResearchEventTypeValue.SearchDone:
          if ((e.message ?? '').startsWith('检索失败')) {
            return { color: 'red', children: <Text type="danger">{e.message}</Text> };
          }
          return {
            color: 'green',
            children: <Text>检索完成：{e.query}（{e.snippetCount ?? 0} 条结果）</Text>,
          };
        case ResearchEventTypeValue.Synthesize:
          return { color: 'blue', children: <Text>正在综合报告…</Text> };
        case ResearchEventTypeValue.Error:
          return { color: 'red', children: <Text type="danger">错误：{e.error}</Text> };
        default:
          return { color: 'gray', children: <Text type="secondary">未知事件</Text> };
      }
    });

  const renderReport = (r: ResearchReport) => (
    <div>
      <Divider orientation="left">调研报告</Divider>
      <Space wrap style={{ marginBottom: 12 }}>
        <Tag color="blue">步骤：{r.stepsUsed}</Tag>
        <Tag color="green">来源：{r.sources.length}</Tag>
        {r.tokenUsage && (
          <Tag color="default">Token：{r.tokenUsage.promptTokens + r.tokenUsage.completionTokens}</Tag>
        )}
      </Space>
      {r.sources.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <Text strong>参考来源</Text>
          <List
            size="small"
            style={{ marginTop: 6 }}
            dataSource={r.sources}
            renderItem={(s) => (
              <List.Item>
                <div>
                  <a href={s.url} target="_blank" rel="noreferrer">
                    {s.title}
                  </a>
                  <div style={{ color: colors.textMuted, fontSize: 12 }}>{s.snippet}</div>
                </div>
              </List.Item>
            )}
          />
        </div>
      )}
      {r.answer && <Paragraph style={{ whiteSpace: 'pre-wrap' }}>{r.answer}</Paragraph>}
      {r.sections.map((sec, i) => (
        <div key={i} style={{ marginBottom: 12 }}>
          <Title level={4} style={{ marginBottom: 4 }}>
            {sec.heading}
          </Title>
          <Paragraph style={{ whiteSpace: 'pre-wrap' }}>{sec.body}</Paragraph>
        </div>
      ))}
    </div>
  );

  return (
    <div>
      <PageHeader
        title="Research（联网多步调研）"
        subtitle="输入一个开放问题，Research Agent 会规划多个检索查询、真实联网检索，并流式返回结构化调研报告。"
      />
      <Card>
        <Alert
          type="info"
          showIcon
          message="检索依赖后端 SerpApi Key 配置；未配置时各查询会返回失败，但报告仍会基于已规划内容生成。"
          style={{ marginBottom: 16 }}
        />
        <Input.TextArea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="输入要调研的问题，例如：2025 年大模型推理成本下降趋势及主要驱动因素"
          autoSize={{ minRows: 3, maxRows: 8 }}
        />
        <Space wrap style={{ marginTop: 12 }}>
          <Input
            value={focus}
            onChange={(e) => setFocus(e.target.value)}
            placeholder="补充侧重方向（可选）"
            style={{ width: 280 }}
            allowClear
          />
          <span>
            <Text type="secondary" style={{ marginRight: 8 }}>
              最大步数
            </Text>
            <InputNumber
              min={1}
              max={8}
              value={maxSteps}
              onChange={(v) => setMaxSteps(v ?? null)}
              style={{ width: 90 }}
            />
          </span>
          <Button
            type="primary"
            icon={<SearchOutlined />}
            loading={running}
            disabled={!question.trim()}
            onClick={handleRun}
          >
            开始调研
          </Button>
          {running && (
            <Button icon={<StopOutlined />} onClick={handleStop}>
              停止
            </Button>
          )}
        </Space>
      </Card>

      <Card style={{ marginTop: 16 }}>
        {errorMsg && <Alert type="error" message={errorMsg} style={{ marginBottom: 12 }} />}
        {timelineItems.length > 0 ? (
          <Timeline items={timelineItems} />
        ) : (
          !running && <Empty description="输入问题后点击「开始调研」，进度会实时显示在这里" />
        )}
        {running && timelineItems.length === 0 && (
          <Text type="secondary">正在规划检索查询…</Text>
        )}
        {report && renderReport(report)}
      </Card>
    </div>
  );
};

export default ResearchPage;
