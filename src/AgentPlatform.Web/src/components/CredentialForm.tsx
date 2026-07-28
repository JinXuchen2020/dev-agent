import React, { useEffect, useState } from 'react';
import {
  Typography,
  Form,
  Select,
  Input,
  AutoComplete,
  Switch,
  Button,
  Space,
  Tooltip,
  message,
} from 'antd';
import { ApiOutlined } from '@ant-design/icons';
import { CredentialCategory } from '../types';
import type {
  TenantCredentialDto,
  CreateTenantCredentialRequest,
  ProviderModelInfo,
} from '../types';
import {
  createTenantCredential,
  updateTenantCredential,
  discoverProviderModels,
  getErrorMessage,
} from '../services/api';
import { useTranslation } from 'react-i18next';

const { Paragraph, Text } = Typography;

// Provider 选项（与 features/model-config.md S1/S6 锁定范围对齐）。
const MODEL_PROVIDERS = ['OpenAI', 'DeepSeek', 'VLLM', 'Custom'];
const SEARCH_PROVIDERS = ['SerpApi'];

// F13 凭据配置子表单：模型 / 搜索 两类同构（name + provider + ApiKey 掩码 + BaseUrl/ModelName + 保存）。
// 抽出为共享组件，供「我的凭据」独立页与 Agent 配置页的「凭据设置」Tab 复用。
// mode='create' 走 POST；mode='edit' 走 PUT /{id}，并支持"留空保留现有密钥"。
// F14：模型类 Model Name 改 AutoComplete + 一键「拉取模型」（填 Key+BaseUrl 后探测 provider 账户可访问模型）。
const CredentialForm: React.FC<{
  category: CredentialCategory;
  mode: 'create' | 'edit';
  editing?: TenantCredentialDto | null;
  onSaved: () => void;
  onCancel: () => void;
}> = ({ category, mode, editing, onSaved, onCancel }) => {
  const { t } = useTranslation();
  const isModel = category === CredentialCategory.Model;
  const [form] = Form.useForm();
  const [saving, setSaving] = useState(false);
  const [mask, setMask] = useState<string | null>(null);

  // F14：模型发现状态。
  const [modelOptions, setModelOptions] = useState<{ value: string; label: string }[]>([]);
  const [discovering, setDiscovering] = useState(false);

  const providerOptions = (isModel ? MODEL_PROVIDERS : SEARCH_PROVIDERS).map((p) => ({
    label: p,
    value: p,
  }));

  // 实时监听表单字段，驱动「拉取模型」按钮可用态。
  const watchedProvider = Form.useWatch('provider', form);
  const watchedApiKey = Form.useWatch('apiKey', form);
  const watchedBaseUrl = Form.useWatch('baseUrl', form);

  // 编辑模式下若用户未重填 API Key，按钮禁用并要求先填 Key（D1：不做后端解密存量密钥探测）。
  const apiKeyFilled = !!watchedApiKey && watchedApiKey.trim().length > 0;
  const baseUrlFilled = !!watchedBaseUrl && watchedBaseUrl.trim().length > 0;
  const needsBaseUrl = watchedProvider === 'VLLM' || watchedProvider === 'Custom';
  const canDiscover =
    isModel && apiKeyFilled && (!needsBaseUrl || baseUrlFilled);
  const discoverDisabledTip = !isModel
    ? ''
    : !apiKeyFilled
      ? t('pages.credentials.fetchNeedKey')
      : needsBaseUrl && !baseUrlFilled
        ? t('pages.credentials.fetchNeedBaseUrl')
        : '';

  // 编辑模式：用传入的凭据回填；创建模式：给默认 provider。
  useEffect(() => {
    if (mode === 'edit' && editing) {
      setMask(editing.apiKeyMask);
      setModelOptions([]);
      form.setFieldsValue({
        name: editing.name,
        provider: editing.provider,
        baseUrl: editing.baseUrl ?? '',
        modelName: editing.modelName ?? '',
        isEnabled: editing.isEnabled,
      });
    } else {
      setMask(null);
      setModelOptions([]);
      form.setFieldsValue({
        name: '',
        provider: isModel ? 'OpenAI' : 'SerpApi',
        baseUrl: '',
        modelName: '',
        isEnabled: true,
      });
    }
  }, [mode, editing, form, isModel]);

  const handleDiscover = () => {
    const provider = watchedProvider;
    const apiKey = watchedApiKey;
    const baseUrl = watchedBaseUrl;
    if (!apiKeyFilled) {
      message.warning(t('pages.credentials.fetchNeedKey'));
      return;
    }
    setDiscovering(true);
    discoverProviderModels({ provider, apiKey, baseUrl: baseUrl || null })
      .then((list: ProviderModelInfo[]) => {
        const opts = list.map((m) => ({
          value: m.id,
          label: m.ownedBy ? `${m.id}（${m.ownedBy}）` : m.id,
        }));
        setModelOptions(opts);
        message.success(t('pages.credentials.fetchedCount', { count: list.length }));
      })
      .catch((err: unknown) => message.error(t('pages.credentials.fetchFailed') + '：' + getErrorMessage(err)))
      .finally(() => setDiscovering(false));
  };

  const onFinish = (values: {
    name: string;
    provider: string;
    apiKey?: string;
    baseUrl?: string;
    modelName?: string;
    isEnabled?: boolean;
  }) => {
    setSaving(true);
    const common = {
      name: values.name,
      provider: values.provider,
      apiKey: values.apiKey || null,
      baseUrl: values.baseUrl || null,
      modelName: isModel ? values.modelName || null : null,
      isEnabled: values.isEnabled ?? true,
    };

    const op =
      mode === 'edit' && editing
        ? updateTenantCredential({ id: editing.id, category, ...common })
        : createTenantCredential({ category, ...common } as CreateTenantCredentialRequest);

    op
      .then((dto) => {
        message.success(mode === 'edit' ? t('pages.credentials.saveUpdated') : t('pages.credentials.saveSuccess'));
        if (dto) setMask(dto.apiKeyMask);
        onSaved();
      })
      .catch((err: unknown) => message.error(t('pages.credentials.saveFailed') + '：' + getErrorMessage(err)))
      .finally(() => setSaving(false));
  };

  return (
    <Form
      form={form}
      layout="vertical"
      onFinish={onFinish}
      initialValues={{ isEnabled: true, provider: isModel ? 'OpenAI' : 'SerpApi' }}
    >
      <Form.Item
        label={t('pages.credentials.nameLabel')}
        name="name"
        rules={[{ required: true, message: t('pages.credentials.nameRequired') }]}
      >
        <Input placeholder={isModel ? t('pages.credentials.namePlaceholderModel') : t('pages.credentials.namePlaceholderSearch')} />
      </Form.Item>
      <Form.Item
        label={t('pages.credentials.providerLabel')}
        name="provider"
        rules={[{ required: true, message: t('pages.credentials.providerRequired') }]}
      >
        <Select options={providerOptions} />
      </Form.Item>
      <Form.Item
        label={t('pages.credentials.apiKeyLabel')}
        name="apiKey"
        extra={
          mode === 'edit' && mask
            ? t('pages.credentials.apiKeyEditHint') + mask + '）'
            : t('pages.credentials.required')
        }
        rules={
          mode === 'edit'
            ? []
            : [{ required: true, message: t('pages.credentials.apiKeyRequired') }]
        }
      >
        <Input.Password
          placeholder={mode === 'edit' ? t('pages.credentials.keepKeyHint') : t('pages.credentials.apiKeyPlaceholder')}
          autoComplete="new-password"
        />
      </Form.Item>
      <Form.Item
        label={t('pages.credentials.baseUrlLabel')}
        name="baseUrl"
        extra={isModel ? t('pages.credentials.baseUrlModelHint') : t('pages.credentials.baseUrlSearchHint')}
      >
        <Input placeholder={isModel ? t('pages.credentials.baseUrlPlaceholderModel') : t('pages.credentials.baseUrlPlaceholderSearch')} />
      </Form.Item>
      {isModel && (
        <Form.Item
          label={t('pages.credentials.modelNameLabel')}
          name="modelName"
          rules={[{ required: true, message: t('pages.credentials.modelNameRequired') }]}
        >
          <AutoComplete
            options={modelOptions}
            placeholder={t('pages.credentials.modelNamePlaceholder')}
            filterOption={(input, option) =>
              (option?.value ?? '').toLowerCase().includes(input.toLowerCase())
            }
          />
        </Form.Item>
      )}
      {isModel && (
        <Form.Item label=" " colon={false}>
          <Space>
            {/* antd 中 disabled Button 不触发 hover，title 不会弹出；用 Tooltip 包裹才能把禁用原因（如「请先填写 API Key」）展示给用户（D1）。 */}
            <Tooltip title={discoverDisabledTip || undefined}>
              <Button
                icon={<ApiOutlined />}
                loading={discovering}
                disabled={!canDiscover}
                onClick={handleDiscover}
              >
                {t('pages.credentials.fetchModels')}
              </Button>
            </Tooltip>
            <Text type="secondary">
              {t('pages.credentials.fetchHint')}
            </Text>
          </Space>
        </Form.Item>
      )}
      <Form.Item label={t('pages.credentials.enabledLabel')} name="isEnabled" valuePropName="checked">
        <Switch />
      </Form.Item>
      <Form.Item>
        <Space>
          <Button type="primary" htmlType="submit" loading={saving}>
            {t('common.save')}
          </Button>
          <Button onClick={onCancel} disabled={saving}>
            {t('common.cancel')}
          </Button>
        </Space>
      </Form.Item>
      {mode === 'edit' && mask && (
        <Paragraph type="secondary">
          {t('pages.credentials.currentMask')}：<Text code>{mask}</Text>
        </Paragraph>
      )}
    </Form>
  );
};

export default CredentialForm;
