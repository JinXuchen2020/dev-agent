// F15 · i18next 初始化（副作用模块，需在任意组件渲染前 import）。
// 默认 zh-CN；语言偏好从 localStorage('app-locale') 恢复，回退 zh-CN。
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { zhCN } from './zh-CN';
import { enUS } from './en-US';
import { DEFAULT_LOCALE, resolveInitialLocale, SUPPORTED_LOCALES } from './config';

const initial = resolveInitialLocale();

i18n.use(initReactI18next).init({
  resources: {
    'zh-CN': { translation: zhCN },
    'en-US': { translation: enUS },
  },
  lng: initial,
  fallbackLng: DEFAULT_LOCALE,
  supportedLngs: SUPPORTED_LOCALES as unknown as string[],
  interpolation: { escapeValue: false }, // React 已防 XSS
  returnNull: false,
});

export { i18n, SUPPORTED_LOCALES };
export type { SupportedLocale } from './config';
