import { describe, it, expect } from 'vitest';
import { statusToTone } from '../theme/tokens';

describe('statusToTone', () => {
  it('maps success variants (en + zh)', () => {
    expect(statusToTone('active')).toBe('success');
    expect(statusToTone('completed')).toBe('success');
    expect(statusToTone('成功')).toBe('success');
    expect(statusToTone('启用')).toBe('success');
  });

  it('maps processing variants', () => {
    expect(statusToTone('running')).toBe('processing');
    expect(statusToTone('进行中')).toBe('processing');
    expect(statusToTone('草稿')).toBe('processing');
  });

  it('maps warning variants', () => {
    expect(statusToTone('expiring')).toBe('warning');
    expect(statusToTone('即将过期')).toBe('warning');
  });

  it('maps error variants', () => {
    expect(statusToTone('failed')).toBe('error');
    expect(statusToTone('revoked')).toBe('error');
    expect(statusToTone('已吊销')).toBe('error');
  });

  it('falls back to neutral for unknown / empty', () => {
    expect(statusToTone('')).toBe('neutral');
    expect(statusToTone('some-unknown-status')).toBe('neutral');
  });
});
