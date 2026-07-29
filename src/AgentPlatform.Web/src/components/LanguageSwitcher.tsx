// F15 · 顶栏语言切换器。即时切换 i18n 语言并持久化到 localStorage。
import { Segmented } from 'antd';
import { GlobalOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { i18n } from '../locales';
import { LANGUAGE_OPTIONS, persistLocale, type SupportedLocale } from '../locales/config';

const LanguageSwitcher: React.FC = () => {
  const { t } = useTranslation();

  const handleChange = (value: string | number) => {
    const locale = value as SupportedLocale;
    void i18n.changeLanguage(locale);
    persistLocale(locale);
  };

  return (
    <Segmented
      aria-label={t('nav.language')}
      value={i18n.language}
      onChange={handleChange}
      options={[
        { label: <span><GlobalOutlined /> {LANGUAGE_OPTIONS[0].label}</span>, value: LANGUAGE_OPTIONS[0].value },
        { label: <span><GlobalOutlined /> {LANGUAGE_OPTIONS[1].label}</span>, value: LANGUAGE_OPTIONS[1].value },
      ]}
    />
  );
};

export default LanguageSwitcher;
