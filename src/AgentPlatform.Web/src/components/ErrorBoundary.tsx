import React from 'react';

interface ErrorBoundaryProps {
  children: React.ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  message: string;
}

/**
 * 全局错误边界：捕获渲染期异常，避免整页白屏，并提供重试入口。
 * 生产环境可在 componentDidCatch 中上报监控。
 */
export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false, message: '' };

  static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
    const message = error instanceof Error ? error.message : String(error);
    return { hasError: true, message };
  }

  componentDidCatch(error: unknown, info: React.ErrorInfo): void {
    console.error('Uncaught UI error:', error, info);
  }

  private handleReset = (): void => {
    this.setState({ hasError: false, message: '' });
  };

  render(): React.ReactNode {
    if (this.state.hasError) {
      return (
        <div style={{ padding: 24, maxWidth: 600, margin: '80px auto', textAlign: 'center' }}>
          <h2>页面出错了</h2>
          <p style={{ color: '#888', marginTop: 8 }}>{this.state.message}</p>
          <button style={{ marginTop: 16 }} onClick={this.handleReset}>
            重试
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}

export default ErrorBoundary;
