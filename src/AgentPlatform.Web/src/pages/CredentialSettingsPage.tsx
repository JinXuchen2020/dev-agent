import React from 'react';
import { Typography, Tabs } from 'antd';
import CredentialManager from '../components/CredentialManager';
import { CredentialCategory } from '../types';
import { useTranslation } from 'react-i18next';

const { Title, Paragraph } = Typography;

// F13 独立凭据页（B 方案：反转 S4「并入 Agent 配置页」决策，新增独立菜单入口）。
// 直达「我的凭据」，按模型 / 搜索 两类分别管理租户自有的全部凭据（支持多条目增删改）。
const CredentialSettingsPage: React.FC = () => {
  const { t } = useTranslation();
  return (
    <div>
      <Title level={4}>{t('pages.credentials.title')}</Title>
      <Paragraph type="secondary">
        {t('pages.credentials.intro')}
      </Paragraph>
      <Tabs
        defaultActiveKey="model"
        items={[
          {
            key: 'model',
            label: t('pages.credentials.modelTab'),
            children: <CredentialManager category={CredentialCategory.Model} />,
          },
          {
            key: 'search',
            label: t('pages.credentials.searchTab'),
            children: <CredentialManager category={CredentialCategory.Search} />,
          },
        ]}
      />
    </div>
  );
};

export default CredentialSettingsPage;
