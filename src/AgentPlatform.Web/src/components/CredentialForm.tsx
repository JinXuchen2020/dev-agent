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
      ? '请先填写 API Key 后再拉取'
      : needsBaseUrl && !baseUrlFilled
        ? 'VLLM / Custom 需先填写 Base URL'
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
      message.warning('请先填写 API Key 后再拉取模型');
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
        message.success(`已拉取 ${list.length} 个模型`);
      })
      .catch((err: unknown) => message.error('拉取失败：' + getErrorMessage(err)))
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
        message.success(mode === 'edit' ? '凭据已更新' : '凭据已添加');
        if (dto) setMask(dto.apiKeyMask);
        onSaved();
      })
      .catch((err: unknown) => message.error('保存失败：' + getErrorMessage(err)))
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
        label="名称"
        name="name"
        rules={[{ required: true, message: '请填写凭据名称（便于在列表中区分）' }]}
      >
        <Input placeholder={isModel ? '如：我的 GPT-4o' : '如：我的 SerpApi'} />
      </Form.Item>
      <Form.Item
        label="Provider"
        name="provider"
        rules={[{ required: true, message: '请选择 Provider' }]}
      >
        <Select options={providerOptions} />
      </Form.Item>
      <Form.Item
        label="API Key"
        name="apiKey"
        extra={
          mode === 'edit' && mask
            ? '留空则保留现有密钥（掩码：' + mask + '）'
            : '必填'
        }
        rules={
          mode === 'edit'
            ? []
            : [{ required: true, message: '请填写 API Key' }]
        }
      >
        <Input.Password
          placeholder={mode === 'edit' ? '留空保留现有密钥' : 'sk-... / 你的密钥'}
          autoComplete="new-password"
        />
      </Form.Item>
      <Form.Item
        label="Base URL"
        name="baseUrl"
        extra={isModel ? 'OpenAI 兼容端点；OpenAI 官方可留空' : 'SerpApi 端点；通常留空'}
      >
        <Input placeholder={isModel ? 'https://api.openai.com/v1 或自定义端点' : 'https://serpapi.com'} />
      </Form.Item>
      {isModel && (
        <Form.Item
          label="Model Name"
          name="modelName"
          rules={[{ required: true, message: '请填写模型名' }]}
        >
          <AutoComplete
            options={modelOptions}
            placeholder="gpt-4o / deepseek-chat / 自定义模型名"
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
                拉取模型
              </Button>
            </Tooltip>
            <Text type="secondary">
              填 Key + Base URL 后，从 provider 账户拉取可访问模型清单
            </Text>
          </Space>
        </Form.Item>
      )}
      <Form.Item label="启用" name="isEnabled" valuePropName="checked">
        <Switch />
      </Form.Item>
      <Form.Item>
        <Space>
          <Button type="primary" htmlType="submit" loading={saving}>
            保存
          </Button>
          <Button onClick={onCancel} disabled={saving}>
            取消
          </Button>
        </Space>
      </Form.Item>
      {mode === 'edit' && mask && (
        <Paragraph type="secondary">
          当前密钥掩码：<Text code>{mask}</Text>
        </Paragraph>
      )}
    </Form>
  );
};

export default CredentialForm;
