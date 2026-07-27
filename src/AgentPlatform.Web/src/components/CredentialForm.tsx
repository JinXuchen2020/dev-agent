import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography,
  Card,
  Spin,
  Form,
  Select,
  Input,
  Switch,
  Button,
  Space,
  message,
} from 'antd';
import { CredentialCategory } from '../types';
import type {
  TenantCredentialDto,
  UpdateTenantCredentialRequest,
} from '../types';
import {
  getTenantCredential,
  updateTenantCredential,
  getErrorMessage,
} from '../services/api';

const { Paragraph, Text } = Typography;

// Provider 选项（与 features/model-config.md S1/S6 锁定范围对齐）。
const MODEL_PROVIDERS = ['OpenAI', 'DeepSeek', 'VLLM', 'Custom'];
const SEARCH_PROVIDERS = ['SerpApi'];

// F13 凭据配置子组件：模型 / 搜索 两类同构（provider + ApiKey 掩码 + BaseUrl/ModelName + 保存）。
// 抽出为共享组件，供「我的凭据」独立页与 Agent 配置页的「凭据设置」Tab 复用。
const CredentialForm: React.FC<{ category: CredentialCategory }> = ({ category }) => {
  const isModel = category === CredentialCategory.Model;
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [hasConfig, setHasConfig] = useState(false);
  const [mask, setMask] = useState<string | null>(null);

  const providerOptions = (isModel ? MODEL_PROVIDERS : SEARCH_PROVIDERS).map((p) => ({
    label: p,
    value: p,
  }));

  const load = useCallback(() => {
    setLoading(true);
    getTenantCredential(category)
      .then((dto: TenantCredentialDto | null) => {
        if (dto) {
          setHasConfig(true);
          setMask(dto.apiKeyMask);
          form.setFieldsValue({
            provider: dto.provider,
            baseUrl: dto.baseUrl ?? '',
            modelName: dto.modelName ?? '',
            isEnabled: dto.isEnabled,
          });
        } else {
          setHasConfig(false);
          setMask(null);
          form.setFieldsValue({
            provider: isModel ? 'OpenAI' : 'SerpApi',
            baseUrl: '',
            modelName: '',
            isEnabled: true,
          });
        }
      })
      .catch((err: unknown) => {
        message.error('加载凭据失败：' + getErrorMessage(err));
      })
      .finally(() => setLoading(false));
  }, [category, form, isModel]);

  useEffect(() => {
    load();
  }, [load]);

  const onFinish = (values: {
    provider: string;
    apiKey?: string;
    baseUrl?: string;
    modelName?: string;
    isEnabled?: boolean;
  }) => {
    setSaving(true);
    const req: UpdateTenantCredentialRequest = {
      category,
      provider: values.provider,
      apiKey: values.apiKey || null,
      baseUrl: values.baseUrl || null,
      modelName: isModel ? values.modelName || null : null,
      isEnabled: values.isEnabled ?? true,
    };
    updateTenantCredential(req)
      .then((dto) => {
        message.success('凭据已保存');
        if (dto) {
          setHasConfig(true);
          setMask(dto.apiKeyMask);
        }
      })
      .catch((err: unknown) => message.error('保存失败：' + getErrorMessage(err)))
      .finally(() => setSaving(false));
  };

  return (
    <Spin spinning={loading}>
      <Card size="small" type="inner" style={{ marginBottom: 16 }}>
      <Paragraph type="secondary" style={{ marginBottom: 12 }}>
        {isModel
          ? '配置后，本租户的对话与 Agent 将使用你自己的模型 API Key（OpenAI 兼容协议）。首次配置需填写 API Key；之后留空则保留现有密钥。'
          : '配置后，本租户的联网调研（Research）将使用你自己的 SerpApi Key。首次配置需填写 API Key；之后留空则保留现有密钥。'}
      </Paragraph>
      {hasConfig && mask && (
        <Paragraph type="secondary" style={{ marginBottom: 12 }}>
          当前密钥掩码：<Text code>{mask}</Text>
        </Paragraph>
      )}
      <Form
        form={form}
        layout="vertical"
        onFinish={onFinish}
        initialValues={{ isEnabled: true, provider: isModel ? 'OpenAI' : 'SerpApi' }}
      >
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
          extra={hasConfig ? '留空则保留现有密钥（掩码：' + (mask ?? '••••') + '）' : '首次配置必填'}
          rules={hasConfig ? [] : [{ required: true, message: '首次配置需填写 API Key' }]}
        >
          <Input.Password
            placeholder={hasConfig ? '留空保留现有密钥' : 'sk-... / 你的密钥'}
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
            <Input placeholder="gpt-4o / deepseek-chat / 自定义模型名" />
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
            <Button onClick={load} disabled={saving}>
              重置
            </Button>
          </Space>
        </Form.Item>
      </Form>
    </Card>
    </Spin>
  );
};

export default CredentialForm;
