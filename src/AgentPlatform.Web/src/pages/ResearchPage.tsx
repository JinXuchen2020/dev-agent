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
import { useTranslation } from 'react-i18next';

const { Text, Paragraph, Title } = Typography;

const ResearchPage: React.FC = () => {
  const { t } = useTranslation();
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
          if (e.type === ResearchEventTypeValue.Error) setErrorMsg(e.error ?? t('pages.research.unknownError'));
        },
        controller.signal,
      );
      } catch (err) {
      const name = (err as { name?: string })?.name;
      if (name !== 'AbortError') {
        message.error(t('pages.research.error') + '：' + getErrorMessage(err));
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
                <Text strong>{t('pages.research.planQueries', { count: e.queries?.length ?? 0 })}</Text>
                <div style={{ marginTop: 6 }}>
                  {(e.queries ?? []).map((q, i) => (
                    <Tag key={i}>{q}</Tag>
                  ))}
                </div>
              </div>
            ),
          };
        case ResearchEventTypeValue.SearchStart:
          return { color: 'blue', children: <Text>{t('pages.research.searching', { query: e.query })}</Text> };
        case ResearchEventTypeValue.SearchDone:
          if ((e.message ?? '').startsWith('检索失败')) {
            return { color: 'red', children: <Text type="danger">{e.message}</Text> };
          }
          return {
            color: 'green',
            children: <Text>{t('pages.research.searchDone', { query: e.query, count: e.snippetCount ?? 0 })}</Text>,
          };
        case ResearchEventTypeValue.Synthesize:
          return { color: 'blue', children: <Text>{t('pages.research.synthesizing')}</Text> };
        case ResearchEventTypeValue.Error:
          return { color: 'red', children: <Text type="danger">{t('pages.research.error') + '：' + e.error}</Text> };
        default:
          return { color: 'gray', children: <Text type="secondary">{t('pages.research.unknownEvent')}</Text> };
      }
    });

  const renderReport = (r: ResearchReport) => (
    <div>
      <Divider orientation="left">{t('pages.research.reportHeading')}</Divider>
      <Space wrap style={{ marginBottom: 12 }}>
        <Tag color="blue">{t('pages.research.steps', { count: r.stepsUsed })}</Tag>
        <Tag color="green">{t('pages.research.sourcesCount', { count: r.sources.length })}</Tag>
        {r.tokenUsage && (
          <Tag color="default">{t('pages.research.token', { count: r.tokenUsage.promptTokens + r.tokenUsage.completionTokens })}</Tag>
        )}
      </Space>
      {r.sources.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <Text strong>{t('pages.research.sources')}</Text>
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
        title={t('pages.research.title')}
        subtitle={t('pages.research.subtitle')}
      />
      <Card>
        <Alert
          type="info"
          showIcon
          message={t('pages.research.alertMsg')}
          style={{ marginBottom: 16 }}
        />
        <Input.TextArea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder={t('pages.research.questionPlaceholder')}
          autoSize={{ minRows: 3, maxRows: 8 }}
        />
        <Space wrap style={{ marginTop: 12 }}>
          <Input
            value={focus}
            onChange={(e) => setFocus(e.target.value)}
            placeholder={t('pages.research.focusPlaceholder')}
            style={{ width: 280 }}
            allowClear
          />
          <span>
            <Text type="secondary" style={{ marginRight: 8 }}>
              {t('pages.research.maxSteps')}
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
            {t('pages.research.start')}
          </Button>
          {running && (
            <Button icon={<StopOutlined />} onClick={handleStop}>
              {t('pages.research.stop')}
            </Button>
          )}
        </Space>
      </Card>

      <Card style={{ marginTop: 16 }}>
        {errorMsg && <Alert type="error" message={errorMsg} style={{ marginBottom: 12 }} />}
        {timelineItems.length > 0 ? (
          <Timeline items={timelineItems} />
        ) : (
          !running && <Empty description={t('pages.research.emptyHint')} />
        )}
        {running && timelineItems.length === 0 && (
          <Text type="secondary">{t('pages.research.planning')}</Text>
        )}
        {report && renderReport(report)}
      </Card>
    </div>
  );
};

export default ResearchPage;
