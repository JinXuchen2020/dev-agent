import React from 'react';
import './index.css'; // 全局 reset：清零 html/body/#root 默认 margin，避免布局溢出产生多余滚动条
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import './locales'; // F15 · 初始化 i18next（须在 App 渲染前 side-effect 导入）
import App from './App';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>,
);
