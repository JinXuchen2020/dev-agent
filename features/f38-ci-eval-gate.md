# F38 · CI YAML 接入评估门禁样例 设计文档

> 来源：F34 评估门禁 · 延后项（`features/f34-online-eval-gate.md` §延后项）。
> 风险等级：🟢 低风险（纯文档 + CI 模板，不触后端/前端源码）。
> 分支：`feat/f38-ci-eval-gate`（2026-09-02 自 `feat/f37-queued-execution` 新建——用户指定基线；F38 交付物与 F34/F37 代码无关，但保持线性可追）。

## 1. 目标

提供**可直接复制使用**的 CI/CD 流水线模板，把 F34 在线评估门禁端点接入 GitHub Actions 与 GitLab CI：模型/prompt/编排配置变更前自动跑数据集回归，未达通过率阈值即阻断合并。配套中文接入指南。

不触任何 `src/` 代码（既有 F34 端点已具备能力，本 feature 仅交付样例与文档）。

## 2. 门禁端点契约（调研事实，2026-09-02）

| 项 | 值 | 位置 |
|---|---|---|
| 路由 | `POST /api/v1/evaluation-datasets/{datasetId}/gate/{workflowId}` | `Api/Controllers/EvaluationDatasetsController.cs:110` |
| 鉴权 | `[Authorize(Roles="Admin,Operator")]`，JWT 经 httpOnly cookie `ap_access_token`（**非** Bearer/localStorage，F2 起） | `EvaluationDatasetsController.cs:109`；`Api/Endpoints/AuthEndpoints.cs:49-56` |
| 登录 | `POST /api/v1/auth/login` body `{email,password}` → Set-Cookie | `AuthEndpoints.cs:16` |
| 请求体 | `{minPassRate?: number}`（null = 用服务端默认）；`Content-Type: application/json` | `EvaluationDatasetsController.cs:142` |
| 阈值解析链 | 请求显式 `minPassRate` > `EvaluationSettings.GateMinPassRate`（默认 **0.8**，appsettings 未覆盖） | `RunEvaluationGateCommand.cs:41`；`EvaluationSettings.cs:19` |
| 结果 | 通过 → **200**；未通过 / 空数据集 → **422**，body `EvaluationGateResult{passed,minPassRate,total,passedCases,score,report}` | `EvaluationDatasetsController.cs:116` |
| 影子语义 | 评估在一次性克隆工作流上执行，零生产写入；用例上限 `EvaluationSettings.MaxCases`（默认 **10**） | `f34` 设计 + `RunEvaluationCommand.cs:42` |
| 越界 | `minPassRate` ∉ [0,1] 抛 `ArgumentOutOfRangeException`；**服务端无 400 映射处理器**（`Program.cs:37-42` 仅注册 6 个 handler）→ 实际返回 **500**。CI 侧 `*` 兜底阻断 + 模板本地数字校验 fail-fast | `RunEvaluationGateCommand.cs:42-43` |

**F41 现状校正（重要）**：backlog F38 原文写「curl 可在本地 **QuickStart** 模式下跑通」——F41（v2.33）已移除 QuickStart 并强制真实 LLM Key fail-fast。本 feature 的本地/CI 验证路径据实调整为：
- **推荐（真实回归）**：指向已部署实例（staging/prod），其环境已配真实 Key → 真跑评估。
- **接线冒烟（确定性，非质量判定）**：本地以 **`Test` 环境**（`StubModelClient`，`Program.cs` 模型校验仅豁免 Test；`Integration` 亦强制真实 Key）起 API，验证登录→调门禁→200/422 分支与阻断逻辑接通；分数恒定，不用于门禁放行。
- **本仓库不自动执行**：模板放 `ci/` 目录（非 `.github/workflows/`），否则会在本仓库 CI 里真的去连一个不存在的目标实例。这一点在指南显式声明。

## 3. 交付物

1. **`ci/eval-gate-github.yml`** — GitHub Actions 可复制模板：
   - 触发：`pull_request`（限定 prompt/模型配置/编排相关 paths）+ `workflow_dispatch`（inputs：`base_url` / `dataset_id` / `workflow_id` / `min_pass_rate`）。
   - job `eval-gate`：`timeout-minutes`；`curl -c` 登录取 cookie → `curl -b` 调门禁 → 按 HTTP 码分支：`200` 放行、`422` 打印 `score/total/passedCases` 后 `exit 1` 阻断、其它（含 `000` 网络失败/`401/403/404`）报错 `exit 1`。
   - secrets：`EVAL_GATE_BASE_URL` / `EVAL_GATE_EMAIL` / `EVAL_GATE_PASSWORD`（+ 可选 repo variable `EVAL_GATE_DATASET_ID`/`EVAL_GATE_WORKFLOW_ID`/`EVAL_GATE_MIN_PASS_RATE`）。
   - 失败通知：配 `SLACK_WEBHOOK_URL` 时经 `slackapi/slack-github-action` 或裸 curl 发阻断告警。
2. **`ci/eval-gate-gitlab.yml`** — GitLab CI 可复制模板：`workflow: rules: merge_request_event` + `include` 用法注释；job `eval-gate`（`before_script` 登录 cookie、`script` 门禁 + 退出码阻断、`allow_failure: false`）；`pages:`/`artifacts: when: on_failure` 落门禁响应 JSON 便于排查；Slack 经 `when: on_failure` job。
3. **`docs/ci-eval-gate-guide.md`** — 中文接入指南：前置（准备数据集、服务账号 Admin/Operator、F41 真实 Key 说明）→ 环境变量/密钥表 → GitHub 接入步骤 → GitLab 接入步骤 → 阈值覆盖（请求体 `minPassRate` > 服务端 `GateMinPassRate`）→ 422/空集恒不通过语义 → 超时与 `MaxCases`（默认 10）→ 失败通知 Slack → 故障排查表（401/403/404/422/000/500）→ 与 F37 队列模式关系（门禁恒同步直跑，决策 D4=A）→ 安全（凭据走 secrets，日志脱敏，勿打印 cookie/token）→ 本仓库不自动生效声明 + 模板校验方法。

## 4. 验收标准

1. `ci/eval-gate-github.yml` / `ci/eval-gate-gitlab.yml` YAML 可被解析（`python -c yaml.safe_load`；真实 CI 用 actionlint / GitLab lint），键结构齐（`on`/`jobs`、`workflow`/`rules`）。
2. 两模板的门禁调用与退出码逻辑一致（200 放行 / 422 阻断 exit 1 / 其它错误 exit 1），且认证走 cookie jar（与 F2 httpOnly cookie 契约一致，不出现 `Authorization: Bearer` 反模式）。
3. 门禁路径/方法/请求体与 F34 端点逐字段一致：`POST /api/v1/evaluation-datasets/{id}/gate/{workflowId}` + `{minPassRate}`。
4. 本地接线冒烟：以 `Test` 环境（Stub 模型）起 API，脚本化验证 200 与 422 两条分支可判别（指南给出可复制命令；若本沙箱无凭据/数据集则以 curl 干跑 + 端点契约说明佐证，并在指南标注）。
5. 指南覆盖：环境变量 / 阈值覆盖 / 失败处理（422→阻断、空集恒不通过、网络/鉴权错误）/ 故障排查 / 安全。
6. 不触 `src/`（无 `.quality-gate.json` 强制；本 feature 仍写一份作追溯）。文档同步：CHANGELOG、deployment-devops 附录、backlog F38 done。

## 5. 决策

- **D1 放置目录 = `ci/`（非 `.github/workflows/`）**：避免模板在本仓库 CI 自动触发去连不存在的目标实例；作为「复制即用」样例。backlog 原路径。
- **D2 认证方式 = cookie jar**：F2 起 JWT 承载于 httpOnly cookie，登录取 `ap_access_token` 后带 cookie 调门禁（与既有中间件契约一致），不用 Bearer。
- **D3 本地验证 = 校正为真实实例 / Stub 冒烟两路径**：F41 已删 QuickStart，backlog 原文「QuickStart 跑通」不可行，据实改（见 §2 校正）。
- **D4 阻断判定 = 以 HTTP 码为唯一契约**（200/422），不解析 report 结构做二次判定（降低与后端 DTO 演进的耦合）；score 仅用于日志展示。

## 6. 风险

- 🟢 纯新增文件 + 文档；唯一实质风险是**模板与 F34 端点漂移** → 指南显式标注「API schema 变更须同步更新本模板」，并在 §4.3 锁定路径/方法/字段。
- 🟡 CI 脚本 `curl` 细节（`-f` 会把 422 当错误吞掉状态码 → 必须用 `-s -o body -w %{http_code}` 显式取码分支）——已在模板中正确实现并注释。

## 7. 审查修复记录（2026-09-02，对抗式评审）

| 严重度 | 位置 | 问题 | 修复 |
|---|---|---|---|
| 高 | `ci/eval-gate-github.yml` 登录/门禁 curl；`ci/eval-gate-gitlab.yml` 同 | curl 传输层失败退出码非 0，`set -e`（GitHub 显式；GitLab runner errexit 语义）抢在 `000` 分支前杀脚本：丢失诊断输出且 cookie jar 不清理 | 两处 `$(curl …)` 追加 `\|\| true`，`-w` 的 000 码进入自写分支 |
| 高 | 两模板登录 body | `${EMAIL}/${PASSWORD}` 未做 JSON 转义直插 body，密码含 `"`/`\` 即破坏载荷（401 误阻断）甚至注入 JSON 字段 | 加 `json_escape`（sed 转义 `\` 与 `"`）构造 `LOGIN_BODY`；GitLab 登录失败路径补 `rm cookie jar` |
| 高 | `ci/eval-gate-github.yml` Notify 步 | `${{ github.ref_name }}`（分支名，外部 PR 可控）内联进 shell → 脚本注入 | 改用 runner 内置 env（`GITHUB_REPOSITORY/REF_NAME/SERVER_URL/RUN_ID`），不进脚本文本 |
| 中 | 两模板 `minPassRate` | 阈值字符串裸插 JSON；且服务端越界实为 500 非 400 | 调用前本地正则校验 `[0,1]` 数字，非数即 fail-fast 阻断 |
| 中 | `docs/ci-eval-gate-guide.md` §1/§7；本文档 §2 | 「越界→400」不实：`ArgumentOutOfRangeException` 无处理器映射（`Program.cs:37-42`）→ 实际 500 | 文档改为 500（`*` 分支兜底阻断，模板行为不变） |
| 中 | `docs/ci-eval-gate-guide.md` §8 | 「登录前用 `X-Workspace-Id` 头指定」错误：`WorkspaceProvider` 优先级 1（JWT claim）压过优先级 2（header），登录恒写租户默认 workspace，header 无法切换非空 claim 会话 | 改为：数据集/工作流须位于租户默认 workspace；登录（匿名）仅 `X-Tenant-Id` 有效 |
| 低 | 本文档 §2 F41 校正 | 「`Test`/`Integration` 环境 Stub 冒烟」不实：`Program.cs:71-94` 仅 Test 豁免，Integration 强制真实 Key | 冒烟路径改为仅 `Test` |
| 低 | `ci/eval-gate-github.yml` 422 分支注释；`ci/eval-gate-gitlab.yml` | 注释称导出 gate-fail.json「给后续通知步骤」，实不消费 | 注释改为「供排查」 |
| 低 | `docs/ci-eval-gate-guide.md` §3 | 未提示 fork PR 拿不到 secrets → 门禁步 fail-fast（误失败预期差） | 补说明与替代触发建议 |

**契约一致性 VERIFIED（逐字段核对源码）**：路由 `api/v1/evaluation-datasets/{id:guid}/gate/{workflowId:guid}`、`[HttpPost]`、`[Authorize(Roles="Admin,Operator")]`、body camelCase `minPassRate`（`Program.cs:20` CamelCase；record `RunEvaluationGateRequest(double? MinPassRate=null)`）、200/422（`EvaluationDatasetsController.cs:116`）、空集恒 422（`RunEvaluationGateCommand.cs:48`）、`GateMinPassRate=0.8`/`MaxCases=10`（`EvaluationSettings.cs:13,19`）、登录 `POST /api/v1/auth/login` `{email,password}`→ httpOnly cookie `ap_access_token`（`AuthEndpoints.cs:16,49-56`）；两模板零 `Authorization: Bearer` 反模式、全程无 `-f`、无 `set -x`、不 echo cookie。修复后两 yml 均通过 `yaml.safe_load` 复核。

## Quality Gate Checklist（ddd-phase-quality-gate，F38 · 2026-09-02）

> 本 feature 不触 `src/`，DDD 分层 / DI / EF 映射类目 N/A；针对 CI 模板 + 文档的可执行子集。

- [x] **Pre-flight**：端点契约核实（路由/方法/RBAC/body camelCase/200·422/越界 500），与 `EvaluationDatasetsController.cs:109-117` + `AuthEndpoints.cs` 逐字段一致。
- [x] **BDD 优先**：无 src/ 变更 → 不新增 BDD；接线判据以 HTTP 码为契约（本地 mock 桩冒烟覆盖 200/422/000 三分支）。
- [x] **分层/位置**：模板落 `ci/`（非 `.github/workflows/`），文档落 `docs/`；不污染本仓库自动 CI。
- [x] **DI/注册**：N/A（无服务）。
- [x] **Configuration-First**：阈值/目标地址/id/凭据全走 CI vars/secrets；无硬编码密钥；`minPassRate` 越界本地 fail-fast。
- [x] **EF/迁移**：N/A。
- [x] **并发/生命周期**：cookie jar mktemp + 用后清理；登录 body 与阈值经 JSON 转义/校验防注入；`set -e` 与 curl `|| true` 协同不吞状态码。
- [x] **横切基建**：422→exit1 阻断语义与 F34 一致；失败通知 Slack 不改变门禁结论；GitHub 外部 PR 分支名经内置 env 不入脚本文本（防注入）。

Gate Status: **PASS**（P0/P1/P2/P3 = 0 open；越界 400→500 文档更正、curl 取码 `|| true`、登录/阈值注入、`ref_name` 注入 已在审查修复记录 §7 收口）。

## codebase-optimizer（Round F38-01，scope=F38-only，2026-09-02）

七维度对 4 文件（2 yml + 2 md）扫描：
- 正确性/安全：登录载荷与阈值注入（json_escape + 本地数字校验）、`ref_name` 脚本注入（内置 env）、`-f` 吞码、secret 入日志 — 均修（见 §7）。VERIFIED。
- 生产就绪度：F41 现状（真实 Key）显式声明；越界实为 500 已校正；fork PR 无 secrets 提示已补。VERIFIED。
- 工程化：CI 配置模板语法（gh `on`/concurrency/permissions；gitlab workflow/rules/include/artifacts/.post）结构校验通过（yaml.safe_load + `bash -n` 内嵌脚本）。
- 桩代码维度：N/A（无 src/ 代码桩）。
- 测试：接线逻辑本地 HTTP 码分支冒烟（200/422/000 实测 rc=0/1/1）。
- 性能：评估同步跑，job timeout 15m 兜底 + MaxCases 提示。

0 open（waiver：真实平台端到端跑需已部署实例+服务账号，超出本地沙箱，指南给可复现命令）。
