# 质量门结论标记（Quality Gate Marker）

本仓库用「质量结论标记」把"跑完质量 skill 且问题清零"这个事实固化下来，供 git 钩子与 CI 卡提交。这是 A+B 档治理的"执行"层——文档里写的是政策，钩子 + CI 是 enforcement。

## 三道质量门禁

2026-07 起，质量门禁从两道扩展为三道：

| 序号 | Skill | 检视对象 | 执行时机 | 强制范围 |
|------|-------|---------|---------|---------|
| 1 | **ddd-code-reviewer** | 高风险模块的代码实现（核对蓝图章节） | 每次高风险模块合入前 | 所有 Phase |
| 2 | **ddd-phase-quality-gate** | DDD 结构卫生（DI/分层/EF/并发/密封/守卫） | 每次高风险模块合入前 | 所有 Phase |
| 3 | **codebase-optimizer** | 全库多维度健康检查（含桩代码替换进度、生产就绪度） | 每阶段**完成后**（阶段最后一笔提交前） | Phase 6+ 强制；Phase 5 过渡期 |

### 设计评审关（前置条件，非提交门禁）

`blueprint-architecture-review` 是**动手编码前的设计评审**，在阶段启动时执行，产出设计评审报告。它不是提交门禁，不进入 `.quality-gate.json` 标记体系。详见 README § "质量治理流程"。

## 什么时候写标记

当你要提交 `src/` 代码改动前，且：

1. **设计评审关**（本阶段启动时已通过，`DESIGN READY` 结论）；
2. 跑 **`ddd-code-reviewer`**（对抗式审查，核对蓝图章节）；
3. 跑 **`ddd-phase-quality-gate`**（DDD 结构卫生）；
4. **阶段完成时**（该阶段最后一笔或倒数第二笔提交前）：跑 **`codebase-optimizer`**（全库健康检查，含桩代码替换进度和生产就绪度）；
5. 三者（或阶段未完成时的前两者）问题均已清零（**0 open findings**，reviewer 报告显式写出"已核对章节"）。

→ 此时把结论写入仓库根 `.quality-gate.json`。

### 过渡期（Phase 5）

Phase 5 已完成且质量门通过，此时 codebase-optimizer 尚未纳入体系。过渡期规则：
- Phase 5 的 `.quality-gate.json` 中 `codebaseOptimizer` 字段值为 `"not_run"`，钩子和 CI 仅校验字段存在，不强制 `PASSED`。
- **Phase 6 及以后**，`codebaseOptimizer` 必须为 `"PASSED (Round X, ...)"` 才放行。

## 标记格式（`.quality-gate.json`，仓库根）

```json
{
  "phase": "phase-2",
  "reviewer": "ddd-code-reviewer PASSED",
  "structureGate": "ddd-phase-quality-gate 0 open",
  "codebaseOptimizer": "codebase-optimizer PASSED (Round 3, 0 open, stub:5/8 replaced, prod-ready:P2)",
  "cleared": true,
  "reportRef": "docs/quality/phase-2-gate.md"
}
```

- `codebaseOptimizer`：记录最近一次 codebase-optimizer 的结果摘要。
  - Phase 6+ 强制包含 `PASSED`。
  - Phase 5 过渡期可写 `"not_run"`（仅用于向后兼容，不得在新阶段使用）。
- `cleared` 必须为 `true` 才放行；否则钩子拒绝提交。
- 建议同时把 reviewer 报告落到 `docs/quality/<phase>-gate.md` 并在 `reportRef` 引用。

## 提交信息格式

commit message 必须含一行质量标记：

```
Quality-Gate: phase-2 cleared (0 open findings) [optimizer: PASSED]
```

- `pre-commit` 校验 `.quality-gate.json` 已暂存且 `cleared: true` + `codebaseOptimizer` 字段存在；
- `commit-msg` 校验 message 含 `Quality-Gate:`。
- 两者互补：文件标记（pre-commit）+ 信息标记（commit-msg）双保险。

## 自动拦截（已落地）

- **本地**：`scripts/git-hooks/pre-commit` + `commit-msg`，在暂存含 `src/` 时强制校验。
  启用：`git config core.hooksPath scripts/git-hooks`（或跑 `scripts/install-hooks.ps1`）。
- **CI**：`.github/workflows/ci.yml` 的 `quality-gate` job，在 push/PR 含 `src/` 改动时同步校验。
- **豁免**：仅改 `docs/` 等文档、不动 `src/` 的提交不受限（文档提交本身不需质量门）。

## 关于"至少跑三轮"

三个 quality skill 是**交互式**的，钩子无法精确判定"跑了 3 轮"。实际可行的卡点是"**reviewer 报告存在且 0 open findings 才放行**"——技能自身的 retry 逻辑通常会让你自然多轮迭代到清零。把质量循环跑到 0 open，再写本标记即可。

## ⚠️ 诚实性原则

`.quality-gate.json` 是**人为维护的声明**，钩子信任它。若 `src/` 实际仍有未修漂移（如当前 Phase 2 旧代码相对新蓝图的漂移），**不应**写 `cleared: true`。标记只在该 Phase 高风险模块确实跑完对应 skill 且清零后才写。
