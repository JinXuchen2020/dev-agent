import { describe, it, expect } from 'vitest';
import { zhCN } from '../locales/zh-CN.ts';
import { enUS } from '../locales/en-US.ts';

// F15 · 资源 key 对称性：zh-CN 与 en-US 的扁平 key 集合必须完全一致，防漏翻。
function flatten(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      return flatten(v as Record<string, unknown>, key);
    }
    return [key];
  });
}

function collectValues(obj: Record<string, unknown>): string[] {
  return Object.values(obj).flatMap((v) => {
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      return collectValues(v as Record<string, unknown>);
    }
    return typeof v === 'string' ? [v] : [];
  });
}

describe('i18n resource symmetry (F15)', () => {
  it('zh-CN and en-US have identical flattened key sets', () => {
    const zh = flatten(zhCN as unknown as Record<string, unknown>).sort();
    const en = flatten(enUS as unknown as Record<string, unknown>).sort();
    expect(en).toEqual(zh);
  });

  // 防回归：默认语言 zh-CN 的用户可见值不得残留英文 "Agent"（应已本地化为「智能体」）。
  it('zh-CN values contain no untranslated "Agent"', () => {
    const leaked = collectValues(zhCN as unknown as Record<string, unknown>).filter((v) =>
      v.includes('Agent'),
    );
    expect(leaked).toEqual([]);
  });
});
