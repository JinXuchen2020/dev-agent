import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Button,
  Drawer,
  Select,
  Input,
  Space,
  Tag,
  Descriptions,
  Empty,
  Spin,
  App as AntApp,
  Modal,
} from 'antd';
import { EyeOutlined, CopyOutlined, AppstoreOutlined } from '@ant-design/icons';
import {
  getWorkflowTemplates,
  getWorkflowTemplateCategories,
  getWorkflowTemplate,
  cloneWorkflowTemplate,
} from '../services/api';
import type {
  WorkflowTemplate,
  WorkflowTemplateCategoryOption,
  WorkflowTemplateDetail,
  WorkflowTemplateCategory,
} from '../types';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import EntityCardGrid from '../components/EntityCardGrid';
import { useAppStore } from '../stores/appStore';
import { useTranslation } from 'react-i18next';

// 8 个分类各配一个稳定色，便于在卡片与筛选器中视觉区分。
const CATEGORY_COLORS: Record<number, string> = {
  0: 'default',
  1: 'blue',
  2: 'green',
  3: 'orange',
  4: 'purple',
  5: 'cyan',
  6: 'magenta',
  7: 'geekblue',
};

// StepType 是「字符串键 → 数值」的 const 对象；此处需要「数值 → 标签」的反查。
const STEP_TYPE_LABELS: Record<number, string> = {
  0: 'Start',
  1: 'End',
  2: 'LLM',
  3: 'Agent',
  4: 'Critic',
  5: 'Knowledge',
  6: 'Tool',
  7: 'Code',
  8: 'Http',
  9: 'Condition',
  10: 'Loop',
  11: 'Variable',
  12: 'SubWorkflow',
  13: 'Delay',
  14: 'UserInput',
};
const stepTypeLabel = (type: number): string => STEP_TYPE_LABELS[type] ?? String(type);

const TemplateMarketPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = AntApp.useApp();
  const userRole = useAppStore((s) => s.userRole);
  const isAdminOrOperator =
    !!userRole && ['admin', 'operator'].includes(userRole.toLowerCase());

  const [list, setList] = useState<WorkflowTemplate[]>([]);
  const [categories, setCategories] = useState<WorkflowTemplateCategoryOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [category, setCategory] = useState<WorkflowTemplateCategory | null>(null);
  const [keyword, setKeyword] = useState<string>('');

  const [preview, setPreview] = useState<WorkflowTemplateDetail | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);

  const [cloningId, setCloningId] = useState<string | null>(null);

  const loadCategories = () => {
    getWorkflowTemplateCategories()
      .then(setCategories)
      .catch(() => message.error(t('errors.loadFailed')));
  };

  const load = () => {
    setLoading(true);
    getWorkflowTemplates({ category, keyword: keyword || null })
      .then(setList)
      .catch(() => message.error(t('errors.loadFailed')))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    loadCategories();
  }, []);

  useEffect(load, [category, keyword]);

  const categoryOptions = useMemo(
    () => [
      { value: null as unknown as number, label: t('common.all') },
      ...categories.map((c) => ({ value: c.value, label: c.name })),
    ],
    [categories, t],
  );

  const openPreview = async (tmpl: WorkflowTemplate) => {
    setPreviewOpen(true);
    setPreviewLoading(true);
    setPreview(null);
    try {
      const detail = await getWorkflowTemplate(tmpl.id);
      setPreview(detail);
    } catch {
      message.error(t('errors.loadFailed'));
    } finally {
      setPreviewLoading(false);
    }
  };

  const handleClone = (tmpl: WorkflowTemplate) => {
    Modal.confirm({
      title: t('pages.templateMarket.cloneConfirmTitle'),
      content: t('pages.templateMarket.cloneConfirmContent', { name: tmpl.name }),
      okText: t('pages.templateMarket.clone'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        setCloningId(tmpl.id);
        try {
          const created = await cloneWorkflowTemplate(tmpl.id);
          message.success(t('pages.templateMarket.cloned'));
          navigate(`/workflows/${created.id}`);
        } catch {
          message.error(t('errors.forbidden'));
        } finally {
          setCloningId(null);
        }
      },
    });
  };

  const renderCard = (tmpl: WorkflowTemplate) => (
    <Card
      title={tmpl.name}
      extra={
        <Tag color={CATEGORY_COLORS[tmpl.category] ?? 'default'}>
          {categories.find((c) => c.value === tmpl.category)?.name ?? String(tmpl.category)}
        </Tag>
      }
    >
      <Space direction="vertical" size={8} style={{ width: '100%' }}>
        <span style={{ color: 'rgba(0,0,0,0.65)', minHeight: 44, display: 'block' }}>
          {tmpl.description || t('pages.templateMarket.noDescription')}
        </span>
        {tmpl.tags.length > 0 && (
          <Space size={[4, 4]} wrap>
            {tmpl.tags.map((tag) => (
              <Tag key={tag} color="blue">
                {tag}
              </Tag>
            ))}
          </Space>
        )}
        <Space
          onClick={(e) => e.stopPropagation()}
          style={{ justifyContent: 'flex-end', width: '100%' }}
        >
          <Button
            size="small"
            icon={<EyeOutlined />}
            onClick={() => openPreview(tmpl)}
          >
            {t('pages.templateMarket.preview')}
          </Button>
          {isAdminOrOperator && (
            <Button
              size="small"
              type="primary"
              icon={<CopyOutlined />}
              loading={cloningId === tmpl.id}
              onClick={() => handleClone(tmpl)}
            >
              {t('pages.templateMarket.clone')}
            </Button>
          )}
        </Space>
      </Space>
    </Card>
  );

  return (
    <div>
      <PageHeader
        title={t('pages.templateMarket.title')}
        subtitle={t('pages.templateMarket.subtitle')}
      />

      <Card
        title={
          <span>
            <AppstoreOutlined style={{ marginRight: 8 }} />
            {t('pages.templateMarket.listTitle')}
          </span>
        }
        extra={
          <Space>
            <Select
              style={{ width: 180 }}
              placeholder={t('pages.templateMarket.filterCategory')}
              value={category}
              options={categoryOptions}
              onChange={(v) => setCategory(typeof v === 'number' ? (v as WorkflowTemplateCategory) : null)}
              allowClear
            />
            <Input.Search
              allowClear
              placeholder={t('pages.templateMarket.searchPlaceholder')}
              onSearch={(v) => setKeyword(v)}
              style={{ width: 220 }}
            />
          </Space>
        }
      >
        <EntityCardGrid
          items={list}
          loading={loading}
          rowKey={(tmpl) => tmpl.id}
          emptyText={t('pages.templateMarket.empty')}
          onItemClick={openPreview}
          renderCard={renderCard}
        />
      </Card>

      <Drawer
        title={preview?.name ?? t('pages.templateMarket.preview')}
        width={520}
        open={previewOpen}
        onClose={() => setPreviewOpen(false)}
      >
        {previewLoading ? (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin />
          </div>
        ) : preview ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label={t('pages.templateMarket.category')}>
                <Tag color={CATEGORY_COLORS[preview.category] ?? 'default'}>
                  {categories.find((c) => c.value === preview.category)?.name ?? String(preview.category)}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('common.description')}>
                {preview.description || t('pages.templateMarket.noDescription')}
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.templateMarket.tag')}>
                {preview.tags.length > 0
                  ? preview.tags.map((tag) => (
                      <Tag key={tag} color="blue">
                        {tag}
                      </Tag>
                    ))
                  : '-'}
              </Descriptions.Item>
            </Descriptions>

            <div>
              <div style={{ fontWeight: 600, marginBottom: 8 }}>
                {t('pages.templateMarket.context')}
              </div>
              <pre
                style={{
                  background: 'rgba(0,0,0,0.03)',
                  borderRadius: 6,
                  padding: 12,
                  fontSize: 12,
                  maxHeight: 160,
                  overflow: 'auto',
                }}
              >
                {preview.context}
              </pre>
            </div>

            <div>
              <div style={{ fontWeight: 600, marginBottom: 8 }}>
                {t('pages.templateMarket.nodes')} ({preview.nodes.length})
              </div>
              {preview.nodes.length === 0 ? (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ) : (
                <Space direction="vertical" size={6} style={{ width: '100%' }}>
                  {preview.nodes.map((n, i) => (
                    <div
                      key={n.id}
                      style={{
                        display: 'flex',
                        gap: 8,
                        alignItems: 'center',
                        padding: '6px 10px',
                        border: '1px solid rgba(0,0,0,0.08)',
                        borderRadius: 6,
                      }}
                    >
                      <span style={{ color: 'rgba(0,0,0,0.45)', width: 20 }}>{i + 1}</span>
                      <Tag color="blue">{stepTypeLabel(n.type)}</Tag>
                      <span>{n.name}</span>
                    </div>
                  ))}
                </Space>
              )}
            </div>

            <div>
              <div style={{ fontWeight: 600, marginBottom: 8 }}>
                {t('pages.templateMarket.edges')} ({preview.edges.length})
              </div>
              {preview.edges.length === 0 ? (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ) : (
                <Space direction="vertical" size={4} style={{ width: '100%' }}>
                  {preview.edges.map((e) => {
                    const from = preview.nodes.find((n) => n.id === e.source)?.name ?? e.source;
                    const to = preview.nodes.find((n) => n.id === e.target)?.name ?? e.target;
                    return (
                      <div key={e.id} style={{ color: 'rgba(0,0,0,0.65)', fontSize: 13 }}>
                        {from} → {to}
                        {e.label ? ` (${e.label})` : ''}
                      </div>
                    );
                  })}
                </Space>
              )}
            </div>

            {isAdminOrOperator && (
              <Button
                type="primary"
                block
                icon={<CopyOutlined />}
                loading={cloningId === preview.id}
                onClick={() => handleClone(preview)}
              >
                {t('pages.templateMarket.clone')}
              </Button>
            )}
          </Space>
        ) : null}
      </Drawer>
    </div>
  );
};

export default TemplateMarketPage;
