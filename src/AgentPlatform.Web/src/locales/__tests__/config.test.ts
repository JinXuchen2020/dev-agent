import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
  SUPPORTED_LOCALES,
  DEFAULT_LOCALE,
  STORAGE_KEY,
  resolveInitialLocale,
  persistLocale,
} from '../config';

describe('i18n locale persistence (F15)', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('falls back to DEFAULT_LOCALE when nothing is stored', () => {
    expect(resolveInitialLocale()).toBe(DEFAULT_LOCALE);
  });

  it('restores a supported stored locale', () => {
    persistLocale('en-US');
    expect(resolveInitialLocale()).toBe('en-US');
  });

  it('ignores an unsupported stored locale', () => {
    localStorage.setItem(STORAGE_KEY, 'fr-FR');
    expect(resolveInitialLocale()).toBe(DEFAULT_LOCALE);
  });

  it('declares exactly the two supported locales', () => {
    expect(SUPPORTED_LOCALES).toEqual(['zh-CN', 'en-US']);
  });
});
