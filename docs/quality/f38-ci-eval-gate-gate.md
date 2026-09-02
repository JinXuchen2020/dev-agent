# F38 · CI YAML 接入评估门禁样例 质量门报告

> 日期：2026-09-02 · 分支 `feat/f38-ci-eval-gate`（基于 `feat/f37-queued-execution`）· feature-builder 流水线
> 设计文档：`features/f38-ci-eval-gate.md`（含 Quality Gate Checklist + 审查修复记录 §7 + optimizer 记录）

## 范围

纯 CI 模板 + 文档（`ci/eval-gate-github.yml`、`ci/eval-gate-gitlab.yml`、`docs/ci-eval-gate-guide.md`），**不触 `src/`**。门禁三focus于 shell/CI 正确性、契约一致性、安全。

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer（对抗式，聚焦 CI/shell） | **PASS**（0 open） | 3×高 + 2×中 修复 |
| ddd-phase-quality-gate | **PASS** | checklist 嵌入设计文档；DI/EF/分层 N/A（无 src） |
| codebase-optimizer | **PASS**（Round F38-01，0 open） | 桩代码维度 N/A；工程化/生产就绪 VERIFIED |

## 修复记录（对抗式评审）

| 严重度 | 位置 | 问题 | 修复 |
|---|---|---|---|
| 高 | 两模板登录 / 门禁 curl | 传输失败退出≠0，`set -e` 抢在 000 分支前杀脚本（丢诊断、cookie jar 不清理） | `$(curl …)` 追加 `\|\| true` |
| 高 | 两模板登录 body | 邮箱/密码裸插 JSON，含 `"`/`\` 破坏载荷甚至注入字段 | `json_escape`（sed 转义 `\`、`"`） |
| 高 | github Notify 步 | `${{ github.ref_name }}`（外部 PR 分支名可控）内联 shell → 脚本注入 | 改用 runner 内置 env，不进脚本文本 |
| 中 | 两模板阈值 | `minPassRate` 裸插 JSON；越界实为 500 非 400 | 调用前本地正则校验 [0,1]，非数即 fail-fast；文档更正 500 |
| 中 | guide/feature | 「越界→400」不实（服务端无 `ArgumentOutOfRangeException` handler，仅注册 6 个 → 500）；「登录前带 X-Workspace-Id 切 workspace」不实（claim 优先级压过 header，登录恒写租户默认） | 均据实更正 |

## 契约 VERIFIED（逐字段对齐真实代码）

`POST /api/v1/evaluation-datasets/{id:guid}/gate/{workflowId:guid}`、`[Authorize(Roles="Admin,Operator")]`、body `minPassRate`（camelCase）、200=通过 / 422=未通过或空集（`EvaluationGateResult.passed=false`）、默认阈值 `GateMinPassRate=0.8`、`MaxCases=10`；登录 `POST /api/v1/auth/login` `{email,password}` → httpOnly cookie `ap_access_token`（无 Bearer/Authorization 反模式）。源：`EvaluationDatasetsController.cs:109-117` / `AuthEndpoints.cs:16-51` / `RunEvaluationGateCommand.cs:41-48` / `Program.cs:37-42`。

## 验证

- 两模板 `python -c yaml.safe_load` 解析通过 + 结构键断言（gh `on/jobs/timeout-minutes/steps`；gitlab `workflow/eval-gate/rules/allow_failure:false`）。
- GitHub 模板内嵌 `run` 脚本 `bash -n` 语法通过。
- HTTP 码阻断逻辑本地 mock 桩冒烟：200→rc0（放行）、422→rc1（阻断）、000→rc1（不可达阻断）。
- 无 `src/` 变更 → 不影响 build/测试基线。

## 已知残留（非阻断）

1. 真实平台端到端跑（含已部署实例 + 服务账号 + 数据集）由接入方环境承担，指南给可复现 curl 命令。
2. `actionlint` / GitLab CI Lint 本机不可用，以 `yaml.safe_load` 结构校验替代，正式接入后由目标 CI 兜底。
