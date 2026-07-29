// F15 · i18n 配置常量。
// 语言偏好持久化到 localStorage（key `app-locale`），默认 zh-CN。

export const SUPPORTED_LOCALES = ['zh-CN', 'en-US'] as const;
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number];

export const DEFAULT_LOCALE: SupportedLocale = 'zh-CN';
export const STORAGE_KEY = 'app-locale';

export const LANGUAGE_OPTIONS: { value: SupportedLocale; label: string }[] = [
  { value: 'zh-CN', label: '中文' },
  { value: 'en-US', label: 'English' },
];

export function resolveInitialLocale(): SupportedLocale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored && (SUPPORTED_LOCALES as readonly string[]).includes(stored)) {
      return stored as SupportedLocale;
    }
  } catch {
    // localStorage 不可用时（隐私模式等）回退默认。
  }
  return DEFAULT_LOCALE;
}

export function persistLocale(locale: SupportedLocale): void {
  try {
    localStorage.setItem(STORAGE_KEY, locale);
  } catch {
    // 忽略持久化失败，不影响运行。
  }
}
