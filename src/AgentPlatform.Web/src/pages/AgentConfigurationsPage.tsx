import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Tag,
  Drawer,
  Descriptions,
  Pagination,
  Space,
  Button,
  Modal,
  Form,
  Input,
  Select,
  Dropdown,
  App as AntApp,
  Popconfirm,
} from 'antd';
import { MoreOutlined, PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { AgentConfiguration, AgentRole } from '../types';
import { AgentConfigurationStatus } from '../types';
import {
  getAgentConfigurations,
  getAgentConfiguration,
  getAgentRoles,
  createAgentConfiguration,
  updateAgentConfiguration,
  deleteAgentConfiguration,
} from '../services/api';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { colors } from '../theme/tokens';

const statusMeta = (
  t: (k: string) => string,
  s: AgentConfigurationStatus,
): { label: string; color: string } => {
  switch (s) {
    case AgentConfigurationStatus.Active:
      return { label: t('pages.configurations.statusActive'), color: 'green' };
    case AgentConfigurationStatus.Archived:
      return { label: t('pages.configurations.statusArchived'), color: 'default' };
    case AgentConfigurationStatus.Deprecated:
      return { label: t('pages.configurations.statusDeprecated'), color: 'red' };
    default:
      return { label: t('pages.configurations.statusDraft'), color: 'gold' };
  }
};

const AgentConfigurationsPage: React.FC = () => {
  const { t } = useTranslation();
  const { message } = AntApp.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const isAdmin = !!userRole && userRole.toLowerCase() === 'admin';

  const [configs, setConfigs] = useState<AgentConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [selected, setSelected] = useState<AgentConfiguration | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerLoading, setDrawerLoading] = useState(false);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AgentConfiguration | null>(null);
  const [roles, setRoles] = useState<AgentRole[]>([]);
  const [modalLoading, setModalLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm<{
    name: string;
    description?: string | null;
    agentTypeCode?: string | null;
    yamlContent: string;
  }>();

  const fetch = useCallback((p: number, ps: number, signal?: AbortSignal) => {
    setLoading(true);
    getAgentConfigurations({ skip: (p - 1) * ps, take: ps, signal })
      .then((d) => {
        setConfigs(d.items);
        setTotal(d.totalCount);
      })
      .catch((err: unknown) => {
        if ((err as { name?: string })?.name !== 'CanceledError')
          console.error('[AgentConfigurations] fetch failed', err);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(page, pageSize, controller.signal);
    return () => controller.abort();
  }, [fetch, page, pageSize]);

  const openDrawer = async (c: AgentConfiguration) => {
    setSelected(c);
    setDrawerOpen(true);
    setDrawerLoading(true);
    try {
      // The list summary omits yamlContent; fetch the detail to render it.
      const detail = await getAgentConfiguration(c.id).catch(() => null);
      setSelected(detail ?? c);
    } finally {
      setDrawerLoading(false);
    }
  };

  const openCreate = async () => {
    setDrawerOpen(false); // close detail drawer when creating new
    setEditing(null);
    setModalOpen(true);
    form.resetFields();
    setModalLoading(true);
    try {
      const r = await getAgentRoles().catch(() => [] as AgentRole[]);
      setRoles(r ?? []);
    } finally {
      setModalLoading(false);
    }
  };

  const openEdit = async (c: AgentConfiguration) => {
    setDrawerOpen(false); // close detail drawer when editing
    setEditing(c);
    setModalOpen(true);
    form.resetFields();
    setModalLoading(true);
    try {
      const [detail, r] = await Promise.all([
        getAgentConfiguration(c.id).catch(() => null),
        getAgentRoles().catch(() => [] as AgentRole[]),
      ]);
      setRoles(r ?? []);
      form.setFieldsValue({
        name: detail?.name ?? c.name,
        description: detail?.description ?? c.description ?? null,
        agentTypeCode: detail?.agentTypeCode ?? c.agentTypeCode ?? undefined,
        yamlContent: detail?.yamlContent ?? '',
      });
    } finally {
      setModalLoading(false);
    }
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    setSubmitting(true);
    try {
      if (editing) {
        await updateAgentConfiguration(editing.id, {
          yamlContent: values.yamlContent,
          name: values.name,
          description: values.description ?? null,
          versionBump: 1, // minor bump on edit
        });
        message.success(t('pages.configurations.updated'));
      } else {
        await createAgentConfiguration({
          name: values.name,
          yamlContent: values.yamlContent,
          description: values.description ?? null,
          agentTypeCode: values.agentTypeCode ?? null,
        });
        message.success(t('pages.configurations.created'));
      }
      setModalOpen(false);
      setEditing(null);
      fetch(page, pageSize);
    } catch (e: unknown) {
      if ((e as { response?: unknown })?.response) {
        message.error(
          t('pages.configurations.saveFailed') +
            '：' +
            ((e as { message?: string }).message ?? t('errors.generic')),
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (c: AgentConfiguration) => {
    try {
      await deleteAgentConfiguration(c.id);
      message.success(t('pages.configurations.deleted'));
      fetch(page, pageSize);
    } catch (e: unknown) {
      message.error(
        t('pages.configurations.deleteFailed') +
          '：' +
          ((e as { message?: string }).message ?? t('errors.generic')),
      );
    }
  };

  const renderConfigCard = (c: AgentConfiguration) => (
    <Card
      title={c.name}
      extra={
        isAdmin ? (
          <Space size={0} onClick={(e) => e.stopPropagation()}>
            <Dropdown
              menu={{ items: [{ key: 'edit', label: t('common.edit') }], onClick: () => openEdit(c) }}
              trigger={['click']}
            >
              <Button size="small" type="text" icon={<MoreOutlined />} aria-label={t('common.edit')} />
            </Dropdown>
            <Popconfirm
              title={t('pages.configurations.deleteConfirm')}
              okText={t('common.delete')}
              cancelText={t('common.cancel')}
              onConfirm={() => handleDelete(c)}
            >
              <Button
                size="small"
                type="text"
                danger
                icon={<DeleteOutlined />}
                aria-label={t('common.delete')}
              />
            </Popconfirm>
          </Space>
        ) : undefined
      }
    >
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colType')}: {c.agentTypeCode ?? '-'}
        </span>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colVersion')}: {c.version}
        </span>
        <Tag color={statusMeta(t, c.status).color}>{statusMeta(t, c.status).label}</Tag>
        <span style={{ color: colors.textMuted, fontSize: 13 }}>
          {t('pages.configurations.colUpdated')}: {new Date(c.updatedAt).toLocaleString()}
        </span>
      </Space>
    </Card>
  );

  return (
    <div>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 16,
        }}
      >
        <Typography.Title level={4} style={{ margin: 0 }}>
          {t('pages.configurations.title')}
        </Typography.Title>
        {isAdmin && (
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('pages.configurations.newDefinition')}
          </Button>
        )}
      </div>

      <EntityCardGrid
        items={configs}
        loading={loading}
        rowKey={(c) => c.id}
        emptyText={t('empty.configurations')}
        onItemClick={(c) => openDrawer(c)}
        renderCard={renderConfigCard}
      />
      {!loading && total > 0 && (
        <Pagination
          style={{ marginTop: 16, textAlign: 'right' }}
          current={page}
          pageSize={pageSize}
          total={total}
          showTotal={(total) => t('common.total', { count: total })}
          onChange={(p, ps) => {
            setPage(p);
            setPageSize(ps);
          }}
        />
      )}

      <Drawer
        title={t('pages.configurations.detail')}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={640}
      >
        {selected && (
          <>
            <Descriptions column={1} bordered size="small" style={{ marginBottom: 16 }}>
              <Descriptions.Item label={t('common.name')}>{selected.name}</Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colType')}>
                {selected.agentTypeCode ?? '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colVersion')}>
                {selected.version}
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.statusLabel')}>
                {statusMeta(t, selected.status).label}
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.configurations.colUpdated')}>
                {new Date(selected.updatedAt).toLocaleString()}
              </Descriptions.Item>
            </Descriptions>
            <pre
              style={{
                background: '#0d1117',
                color: '#e6edf3',
                padding: 16,
                borderRadius: 8,
                overflow: 'auto',
                maxHeight: 420,
                fontSize: 12,
                lineHeight: 1.5,
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                margin: 0,
              }}
            >
              {drawerLoading ? t('common.loading') : (selected.yamlContent ?? '')}
            </pre>
          </>
        )}
      </Drawer>

      <Modal
        title={editing ? t('pages.configurations.editDefinition') : t('pages.configurations.newDefinition')}
        open={modalOpen}
        onOk={handleSubmit}
        confirmLoading={submitting}
        onCancel={() => {
          setModalOpen(false);
          setEditing(null);
        }}
        destroyOnHidden
        okText={t('common.save')}
        cancelText={t('common.cancel')}
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="name"
            label={t('pages.configurations.nameLabel')}
            rules={[{ required: true, message: t('common.required') }]}
          >
            <Input placeholder={t('pages.configurations.nameLabel')} />
          </Form.Item>
          <Form.Item name="description" label={t('pages.configurations.descriptionLabel')}>
            <Input.TextArea rows={2} placeholder={t('pages.configurations.descriptionLabel')} />
          </Form.Item>
          <Form.Item name="agentTypeCode" label={t('pages.configurations.typeLabel')}>
            <Select
              allowClear
              loading={modalLoading}
              placeholder={t('pages.configurations.typeLabel')}
              options={roles.map((r) => ({ label: r.name || r.roleCode, value: r.roleCode }))}
            />
          </Form.Item>
          <Form.Item
            name="yamlContent"
            label={t('pages.configurations.yamlLabel')}
            rules={[{ required: true, message: t('common.required') }]}
          >
            <Input.TextArea
              rows={10}
              style={{ fontFamily: 'monospace' }}
              placeholder={t('pages.configurations.yamlPlaceholder')}
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default AgentConfigurationsPage;
