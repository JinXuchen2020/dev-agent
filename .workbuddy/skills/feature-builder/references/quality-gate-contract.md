# 质量门禁契约（feature-builder 引用）

本文件定义 feature-builder 在 Phase 5/6 必须满足的硬性提交契约，源自仓库 `scripts/git-hooks/pre-commit` 与 `.quality-gate.json` 既有格式。Phase 5/6 执行前先读本文件。

## 1. pre-commit 强制规则（scripts/git-hooks/pre-commit）
凡 `git diff --cached` 含 `^src/`：
- 必须同时暂存 `.quality-gate.json`，否则拒绝（提示「质量门未通过：... 未一同暂存」）。
- `.quality-gate.json` 中 `"cleared": true` 必须为 true，否则拒绝。
- 必须含 `"codebaseOptimizer"` 字段（Phase 5 过渡期可 `not_run`，Phase 6+ 需 `PASSED`）。
仅改 docs/ 等、不动 src/ 的提交不受此门限制（文档提交本身不需质量门）。

启用：`git config core.hooksPath scripts/git-hooks`（或 `scripts/install-hooks.ps1` 一键安装）。

## 2. .quality-gate.json 字段契约
```json
{
  "phase": "<feature-id>",
  "reviewer": "ddd-code-reviewer PASSED (<P0/P1/P2=0 open 摘要，含关键修复点>)",
  "structureGate": "ddd-phase-quality-gate PASS (<P0/P1/P2/P3=0 open；checklist 已嵌入 features/<feature-id>.md §6>)",
  "codebaseOptimizer": "codebase-optimizer PASSED (Round N, 0 open; <后端/前端修复项>)  或  Phase5 过渡期 not_run",
  "cleared": true,
  "reportRef": "docs/quality/<feature-id>-gate.md",
  "notes": "<实现摘要：后端/前端/模型一致性>"
}
```
注：`reviewer` / `structureGate` / `codebaseOptimizer` 三个字段的内容是给人的摘要，但**字段名本身必须存在**；pre-commit 仅校验 `cleared` 与 `codebaseOptimizer` 字段存在性，但 ddd 两 skill 会校验 0 open 才允许置 cleared:true。

## 3. 提交信息格式（pre-commit 约定含 Quality-Gate: 行）
```
feat(<feature-id>): <一句话描述>

Quality-Gate: ddd-code-reviewer + ddd-phase-quality-gate + codebase-optimizer PASSED (cleared:true)
- 后端：...
- 前端：...
- 模型一致性：字段/类型/枚举已对齐，tsc + 联调通过
```
缺少 `Quality-Gate:` 行虽不触发 pre-commit 拒绝，但属于项目约定，必须带，便于回溯。

## 4. 三道质量 skill 调用方式
通过 Skill 工具按名调用，严格顺序：
1. **ddd-code-reviewer** — 对抗式审查，修复至 0 open findings。
2. **ddd-phase-quality-gate** — 阶段结构门，把 checklist 嵌入 feature 设计文档 §6，P0–P3 = 0 open。
3. **codebase-optimizer** — 多轮优化（stub 替换/生产就绪），跑至 0 open；结论写 `codebaseOptimizer` 字段。
每道门按其自身 SKILL.md 指引执行；跑完把结论浓缩进 `.quality-gate.json` 对应字段。

## 5. check-in 暂存清单（必须一起 git add）
- 所有 `src/` 改动
- `.quality-gate.json`（已 `cleared:true` + `codebaseOptimizer` 字段）
- `features/<feature-id>.md`（设计文档）
- `docs/quality/<feature-id>-gate.md`（质量报告）
遗漏任一项 → pre-commit 拒绝或后续审计断链。

## 6. 失败回退
若 `git commit` 被 pre-commit 拒绝：
- 报「未一同暂存」→ 确认 `.quality-gate.json` 已 `git add`。
- 报「cleared 非 true」→ 回到 Phase 5 把三道门跑至 0 open 再置 `cleared:true`。
- 报「缺 codebaseOptimizer 字段」→ 补跑 codebase-optimizer 并写入该字段（过渡期可 `not_run`）。
