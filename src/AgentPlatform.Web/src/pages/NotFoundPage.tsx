import React from 'react';
import { Result, Button } from 'antd';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

/**
 * 404 兜底页（O11）：未知路由统一渲染，避免白屏。
 */
const NotFoundPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  return (
    <Result
      status="404"
      title="404"
      subTitle={t('errors.notFound')}
      extra={
        <Button type="primary" onClick={() => navigate('/')}>
          {t('pages.notFound.back')}
        </Button>
      }
    />
  );
};

export default NotFoundPage;
