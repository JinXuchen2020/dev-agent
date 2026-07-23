import React from 'react';
import { Alert, Button, Space } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';

interface ErrorStateProps {
  message: string;
  description?: string;
  onRetry?: () => void;
  retryText?: string;
}

/**
 * 统一错误态出口（O5）：可读 message + 可选重试按钮，替代各处静默吞错。
 */
const ErrorState: React.FC<ErrorStateProps> = ({ message, description, onRetry, retryText = '重试' }) => (
  <Alert
    type="error"
    showIcon
    message={message}
    description={description}
    style={{ margin: '24px 0' }}
    action={
      onRetry ? (
        <Space>
          <Button icon={<ReloadOutlined />} size="small" onClick={onRetry}>
            {retryText}
          </Button>
        </Space>
      ) : undefined
    }
  />
);

export default ErrorState;
