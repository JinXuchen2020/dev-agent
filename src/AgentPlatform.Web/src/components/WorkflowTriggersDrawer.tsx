import React, { useEffect, useState } from 'react';
import {
  Drawer,
  Typography,
  Input,
  Button,
  Switch,
  Select,
  Tag,
  Space,
  App,
  Popconfirm,
  Divider,
  Alert,
  Spin,
} from 'antd';
import { CopyOutlined, ThunderboltOutlined, LinkOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  getWorkflowTriggers,
  generateWebhookToken,
  disableWebhookTrigger,
  putScheduleTrigger,
  getErrorMessage,
} from '../services/api';
import type { WorkflowTriggersResponse } from '../types';

const { Title, Paragraph, Text } = Typography;

// 常用 IANA 时区（与后端 Cronos 评估一致）。
const COMMON_TIMEZONES = [
  'UTC',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Asia/Singapore',
  'Europe/London',
  'Europe/Paris',
  'America/New_York',
  'America/Los_Angeles',
];

interface Props {
  workflowId: string;
  workflowName: string;
  open: boolean;
  onClose: () => void;
  canManage: boolean;
}

const WorkflowTriggersDrawer: React.FC<Props> = ({
  workflowId,
  workflowName,
  open,
  onClose,
  canManage,
}) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [triggers, setTriggers] = useState<WorkflowTriggersResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [genLoading, setGenLoading] = useState(false);
  const [disableLoading, setDisableLoading] = useState(false);
  const [scheduleLoading, setScheduleLoading] = useState(false);
  const [cron, setCron] = useState('0 0 * * *');
  const [timezone, setTimezone] = useState('UTC');
  const [enabled, setEnabled] = useState(true);

  const load = () => {
    setLoading(true);
    getWorkflowTriggers(workflowId)
      .then((d) => {
        setTriggers(d);
        if (d.schedule) {
          setCron(d.schedule.cron || '0 0 * * *');
          setTimezone(d.schedule.timezone || 'UTC');
          setEnabled(d.schedule.enabled);
        }
      })
      .catch((e) => message.error(getErrorMessage(e)))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    if (open && workflowId) load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, workflowId]);

  const handleGenerate = async () => {
    setGenLoading(true);
    try {
      const res = await generateWebhookToken(workflowId);
      message.success(t('pages.workflows.triggers.webhookGenerated'));
      setTriggers((prev) => ({
        webhook: { triggerToken: res.triggerToken, enabled: true },
        schedule: prev?.schedule ?? null,
        chatBindingCount: prev?.chatBindingCount ?? 0,
      }));
    } catch (e) {
      message.error(getErrorMessage(e));
    } finally {
      setGenLoading(false);
    }
  };

  const handleDisable = async () => {
    setDisableLoading(true);
    try {
      await disableWebhookTrigger(workflowId);
      message.success(t('pages.workflows.triggers.webhookDisabled'));
      setTriggers((prev) => ({
        webhook: { triggerToken: prev?.webhook?.triggerToken ?? null, enabled: false },
        schedule: prev?.schedule ?? null,
        chatBindingCount: prev?.chatBindingCount ?? 0,
      }));
    } catch (e) {
      message.error(getErrorMessage(e));
    } finally {
      setDisableLoading(false);
    }
  };

  const handleSaveSchedule = async () => {
    if (!cron.trim()) {
      message.warning(t('pages.workflows.triggers.cronRequired'));
      return;
    }
    setScheduleLoading(true);
    try {
      const res = await putScheduleTrigger(workflowId, { cron: cron.trim(), timezone, enabled });
      message.success(t('pages.workflows.triggers.scheduleSaved'));
      setTriggers((prev) => ({
        webhook: prev?.webhook ?? null,
        schedule: res,
        chatBindingCount: prev?.chatBindingCount ?? 0,
      }));
    } catch (e) {
      message.error(getErrorMessage(e));
    } finally {
      setScheduleLoading(false);
    }
  };

  const copyToken = async () => {
    const token = triggers?.webhook?.triggerToken;
    if (!token) return;
    try {
      await navigator.clipboard.writeText(token);
      message.success(t('common.copied'));
    } catch {
      message.warning(token);
    }
  };

  const callbackUrl =
    typeof window !== 'undefined' && triggers?.webhook?.triggerToken
      ? `${window.location.origin}/api/v1/webhooks/workflow/${triggers.webhook.triggerToken}`
      : '';

  return (
    <Drawer
      title={t('pages.workflows.triggers.drawerTitle', { name: workflowName })}
      open={open}
      onClose={onClose}
      width={560}
    >
      {loading ? (
        <div style={{ textAlign: 'center', padding: 32 }}>
          <Spin />
        </div>
      ) : (
        <>
          {/* ── Webhook ── */}
          <Title level={5}>
            <ThunderboltOutlined /> {t('pages.workflows.triggers.webhook')}
          </Title>
          <Paragraph type="secondary">{t('pages.workflows.triggers.webhookDesc')}</Paragraph>
          {triggers?.webhook?.triggerToken ? (
            <Space direction="vertical" style={{ width: '100%' }} size={8}>
              <Input
                readOnly
                value={triggers.webhook.triggerToken}
                addonAfter={
                  <Button
                    type="text"
                    size="small"
                    icon={<CopyOutlined />}
                    onClick={copyToken}
                    aria-label={t('common.copy')}
                  />
                }
              />
              <Space wrap>
                <Tag color={triggers.webhook.enabled ? 'success' : 'default'}>
                  {triggers.webhook.enabled
                    ? t('pages.workflows.triggers.enabled')
                    : t('pages.workflows.triggers.disabled')}
                </Tag>
                {triggers.webhook.enabled && canManage && (
                  <Popconfirm
                    title={t('pages.workflows.triggers.disableConfirm')}
                    onConfirm={handleDisable}
                    okText={t('common.confirm')}
                    cancelText={t('common.cancel')}
                  >
                    <Button danger size="small" loading={disableLoading}>
                      {t('pages.workflows.triggers.disable')}
                    </Button>
                  </Popconfirm>
                )}
              </Space>
              {callbackUrl && (
                <Alert
                  type="info"
                  showIcon
                  icon={<LinkOutlined />}
                  message={t('pages.workflows.triggers.callbackUrl')}
                  description={
                    <Text copyable style={{ fontSize: 12 }}>
                      {callbackUrl}
                    </Text>
                  }
                />
              )}
            </Space>
          ) : (
            <Space>
              <Text type="secondary">{t('pages.workflows.triggers.noWebhook')}</Text>
              {canManage && (
                <Button
                  type="primary"
                  icon={<ThunderboltOutlined />}
                  loading={genLoading}
                  onClick={handleGenerate}
                >
                  {t('pages.workflows.triggers.generate')}
                </Button>
              )}
            </Space>
          )}

          <Divider />

          {/* ── Schedule ── */}
          <Title level={5}>{t('pages.workflows.triggers.schedule')}</Title>
          <Paragraph type="secondary">{t('pages.workflows.triggers.scheduleDesc')}</Paragraph>
          <Space direction="vertical" style={{ width: '100%' }} size={10}>
            <div>
              <Text>{t('pages.workflows.triggers.cron')}</Text>
              <Input
                value={cron}
                disabled={!canManage}
                onChange={(e) => setCron(e.target.value)}
                placeholder="0 0 * * *"
                style={{ marginTop: 4 }}
              />
            </div>
            <div>
              <Text>{t('pages.workflows.triggers.timezone')}</Text>
              <Select
                style={{ width: '100%', marginTop: 4 }}
                value={timezone}
                disabled={!canManage}
                onChange={setTimezone}
                options={COMMON_TIMEZONES.map((z) => ({ value: z, label: z }))}
                showSearch
              />
            </div>
            <Space>
              <Text>{t('pages.workflows.triggers.enabled')}</Text>
              <Switch checked={enabled} disabled={!canManage} onChange={setEnabled} />
            </Space>
            {triggers?.schedule?.nextRunAt && (
              <Text type="secondary">
                {t('pages.workflows.triggers.nextRun')}:{' '}
                {new Date(triggers.schedule.nextRunAt).toLocaleString()}
              </Text>
            )}
            {canManage && (
              <Button type="primary" loading={scheduleLoading} onClick={handleSaveSchedule}>
                {t('common.save')}
              </Button>
            )}
          </Space>

          <Divider />

          {/* ── Chat 触发器说明 ── */}
          <Title level={5}>{t('pages.workflows.triggers.chat')}</Title>
          <Paragraph type="secondary">{t('pages.workflows.triggers.chatDesc')}</Paragraph>
          <Text>
            {t('pages.workflows.triggers.chatBindingCount', {
              count: triggers?.chatBindingCount ?? 0,
            })}
          </Text>
        </>
      )}
    </Drawer>
  );
};

export default WorkflowTriggersDrawer;
