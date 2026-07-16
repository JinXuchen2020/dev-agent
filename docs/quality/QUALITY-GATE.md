# 质量门结论标记（Quality Gate Marker）

本仓库用「质量结论标记」把"跑完质量 skill 且问题清零"这个事实固化下来，供 git 钩子与 CI 卡提交。这是 A+B 档治理的"执行"层——文档里写的是政策，钩子 + CI 是 enforcement。

## 什么时候写

当你要提交 `src/` 代码改动前，且已对该 Phase 的高风险模块：

1. 跑 `ddd-code-reviewer`（对抗式审查，核对蓝图章节）；
2. 跑 `ddd-phase-quality-gate`（DDD 结构卫生）；
3. 两者问题均已清零（**0 open findings**，reviewer 报告显式写出"已核对章节"）。

→ 此时把结论写入仓库根 `.quality-gate.json`。

## 标记格式（`.quality-gate.json`，仓库根）

```json
{
  "phase": "phase-2",
  "reviewer": "ddd-code-reviewer PASSED",
  "structureGate": "ddd-phase-quality-gate 0 open",
  "cleared": true,
  "updatedAt": "2026-07-16T11:00:00+08:00",
  "reportRef": "docs/quality/phase-2-gate.md"
}
```

- `cleared` 必须为 `true` 才放行；否则钩子拒绝提交。
- 建议同时把 reviewer 报告落到 `docs/quality/<phase>-gate.md` 并在 `reportRef` 引用。

## 提交信息格式

commit message 必须含一行质量标记：

```
Quality-Gate: phase-2 cleared (0 open findings)
```

- `pre-commit` 校验 `.quality-gate.json` 已暂存且 `cleared: true`；
- `commit-msg` 校验 message 含 `Quality-Gate:`。
- 两者互补：文件标记（pre-commit）+ 信息标记（commit-msg）双保险。

## 自动拦截（已落地）

- **本地**：`scripts/git-hooks/pre-commit` + `commit-msg`，在暂存含 `src/` 时强制校验。
  启用：`git config core.hooksPath scripts/git-hooks`（或跑 `scripts/install-hooks.ps1`）。
- **CI**：`.github/workflows/ci.yml` 的 `quality-gate` job，在 push/PR 含 `src/` 改动时同步校验。
- **豁免**：仅改 `docs/` 等文档、不动 `src/` 的提交不受限（文档提交本身不需质量门）。

## 关于"至少跑三轮"

两个 quality skill 是**交互式**的，钩子无法精确判定"跑了 3 轮"。实际可行的卡点是"**reviewer 报告存在且 0 open findings 才放行**"——技能自身的 retry 逻辑通常会让你自然多轮迭代到清零。把质量循环跑到 0 open，再写本标记即可。

## ⚠️ 诚实性原则

`.quality-gate.json` 是**人为维护的声明**，钩子信任它。若 `src/` 实际仍有未修漂移（如当前 Phase 2 旧代码相对新蓝图的漂移），**不应**写 `cleared: true`。标记只在该 Phase 高风险模块确实跑完两个 skill 且清零后才写。
