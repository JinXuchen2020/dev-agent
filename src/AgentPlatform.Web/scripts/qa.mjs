#!/usr/bin/env node
// 前端自主 QA 闭环运行器
// 顺序跑：typecheck → lint → build → unit → (e2e)
// 任一闸门失败则退出码非 0，并把结构化报告写入 qa-report.json，
// 供「自主修复」代理读取失败片段后定位根因、打补丁、再回归。
import { spawnSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const ROOT = process.cwd();
const runE2E = process.argv.includes('--e2e');

const steps = [
  { name: 'typecheck', cmd: 'npx', args: ['tsc', '--noEmit'] },
  { name: 'lint', cmd: 'npx', args: ['eslint', '.', '--format', 'json'] },
  { name: 'build', cmd: 'npm', args: ['run', 'build'] },
  { name: 'unit', cmd: 'npx', args: ['vitest', 'run', '--reporter', 'json'] },
];
if (runE2E) {
  steps.push({ name: 'e2e', cmd: 'npx', args: ['playwright', 'test', '--reporter', 'line'] });
}

const results = [];
let failed = false;

for (const step of steps) {
  const start = Date.now();
  const r = spawnSync(step.cmd, step.args, {
    cwd: ROOT,
    encoding: 'utf8',
    shell: true,
    maxBuffer: 64 * 1024 * 1024,
  });
  const elapsed = Date.now() - start;
  const out = `${r.stdout || ''}\n${r.stderr || ''}`;
  const code = r.status ?? (r.error ? 1 : 0);
  const pass = code === 0;

  let summary = '';
  if (step.name === 'lint' && !pass) {
    try {
      const json = JSON.parse(r.stdout || '[]');
      const errs = json.flatMap((f) => (f.messages || []).filter((m) => m.severity === 2));
      summary = `${errs.length} lint error(s)`;
    } catch {
      summary = 'lint produced no parseable JSON';
    }
  }

  // 失败片段：保留最后 2500 字符，供代理定位
  const snippet = out.slice(-2500);
  results.push({ step: step.name, pass, code, elapsedMs: elapsed, summary, snippet });
  if (!pass) failed = true;
}

const report = { failed, steps: results, at: new Date().toISOString() };
writeFileSync(resolve(ROOT, 'qa-report.json'), JSON.stringify(report, null, 2));

console.log('\n===== QA GATE SUMMARY =====');
for (const r of results) {
  console.log(
    `${r.pass ? 'PASS' : 'FAIL'}  ${r.step.padEnd(10)} (${r.elapsedMs}ms)${r.summary ? '  ' + r.summary : ''}`,
  );
}
console.log(`\nOVERALL: ${failed ? 'FAIL' : 'PASS'}`);
console.log('Report written to qa-report.json');

process.exit(failed ? 1 : 0);
