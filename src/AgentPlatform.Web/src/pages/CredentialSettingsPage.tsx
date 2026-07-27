import React from 'react';
import { Typography, Tabs } from 'antd';
import CredentialManager from '../components/CredentialManager';
import { CredentialCategory } from '../types';

const { Title, Paragraph } = Typography;

// F13 独立凭据页（B 方案：反转 S4「并入 Agent 配置页」决策，新增独立菜单入口）。
// 直达「我的凭据」，按模型 / 搜索 两类分别管理租户自有的全部凭据（支持多条目增删改）。
const CredentialSettingsPage: React.FC = () => {
  return (
    <div>
      <Title level={4}>我的凭据</Title>
      <Paragraph type="secondary">
        在此管理本租户自有的外部服务密钥（BYO-Key）。模型密钥用于对话与 Agent 调用，可添加多个不同模型；
        搜索密钥用于联网调研（Research）。密钥加密存储，页面仅展示掩码。
      </Paragraph>
      <Tabs
        defaultActiveKey="model"
        items={[
          {
            key: 'model',
            label: '模型',
            children: <CredentialManager category={CredentialCategory.Model} />,
          },
          {
            key: 'search',
            label: '搜索',
            children: <CredentialManager category={CredentialCategory.Search} />,
          },
        ]}
      />
    </div>
  );
};

export default CredentialSettingsPage;
