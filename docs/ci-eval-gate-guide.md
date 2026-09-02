# F38 · 评估门禁 CI/CD 接入指南

本指南说明如何用仓库 `ci/` 下的两个模板，把 **F34 在线评估门禁**接入 GitHub Actions / GitLab CI，实现「模型 / prompt / 编排配置变更前自动回归，未达阈值阻断合并」。

配套文件：
- `ci/eval-gate-github.yml` — GitHub Actions 模板
- `ci/eval-gate-gitlab.yml` — GitLab CI 模板
- 设计文档：`features/f38-ci-eval-gate.md`

---

## 1. 门禁端点（契约）

```
POST /api/v1/evaluation-datasets/{datasetId}/gate/{workflowId}
Content-Type: application/json
Cookie: ap_access_token=<JWT>        # 由 /auth/login 签发（httpOnly cookie，非 Bearer）

{ "minPassRate": 0.9 }                # 可选；缺省用服务端 EvaluationSettings.GateMinPassRate（默认 0.8）
```

响应：

| HTTP | 语义 | 流水线动作 |
|---|---|---|
| 200 | 通过率 ≥ 阈值 | 放行 |
| 422 | 通过率 < 阈值 **或数据集为空**（`body.passed=false`） | **阻断（exit 1）** |
| 401 / 403 | 未认证 / 角色非 Admin·Operator | 阻断 |
| 404 | 数据集或工作流不存在 | 阻断 |
| 000 | 连不上目标实例 | 阻断 |
| 其它（如 500） | 服务端异常。注意：`minPassRate` ∉ [0,1] 抛 `ArgumentOutOfRangeException`，服务端**未注册** → 400 的映射，实际返回 **500**（模板两分支的 `*` 兜底同样阻断；模板已加本地数字校验先行 fail-fast） | 阻断 |

判定**只以 HTTP 码为准**（不解析 report 结构二次判断，降低与后端 DTO 演进的耦合）；`score/total/passedCases` 仅用于日志与排查。影子语义：评估在一次性克隆的工作流上跑，零生产写入；用例数上限 `EvaluationSettings.MaxCases`（默认 10）。

> ⚠️ `ci/` 下的模板不会被本仓库自动执行（避免连一个不存在的目标实例）。必须复制到目标项目并配好 secrets 才生效。

---

## 2. 前置条件

1. **目标实例已部署且配了真实 LLM Key**（F41 起无 QuickStart/无 Stub 兜底，缺失即启动 fail-fast；评估会真实调用模型）。CI 通常指向 **staging**。
2. **服务账号**：一个具备 `Admin` 或 `Operator` 角色的用户（门禁 RBAC 要求）。建议专用 CI 账号、禁用 MFA、密码放 CI secret。
3. **评估数据集与工作流**：在目标实例上预先创建好（可在应用「评估数据集」页或经 API `POST /api/v1/evaluation-datasets` 建，`GET /api/v1/evaluation-datasets` 拿 id），记下 `datasetId` 与 `workflowId` 两个 GUID。
4. 目标实例地址、上述 id 与凭据作为 CI 变量/密钥注入。

### 环境变量 / 密钥一览

| 名称 | 必填 | 说明 |
|---|---|---|
| `EVAL_GATE_BASE_URL` | 是 | 目标实例根地址，如 `https://staging.example.com` |
| `EVAL_GATE_EMAIL` | 是 | 服务账号邮箱 |
| `EVAL_GATE_PASSWORD` | 是 | 服务账号密码（GitHub secret / GitLab masked variable） |
| `EVAL_GATE_DATASET_ID` | 是 | 评估数据集 GUID |
| `EVAL_GATE_WORKFLOW_ID` | 是 | 被回归的工作流 GUID |
| `EVAL_GATE_MIN_PASS_RATE` | 否 | 阈值 0–1；不填用服务端默认 0.8 |
| `SLACK_WEBHOOK_URL` / `EVAL_GATE_SLACK_WEBHOOK_URL` | 否 | 配则门禁失败时发通知 |

安全：凭据只走 secrets/masked variables；模板**绝不** `echo` cookie 或 token；用 `curl -w '%{http_code}'` 取码而非 `-f`（`-f` 会把 422 当普通错误吞掉状态码，无法区分「未通过」与「网络失败」）。

---

## 3. GitHub Actions 接入

1. 复制 `ci/eval-gate-github.yml` → 目标仓库 `.github/workflows/eval-gate.yml`。
2. 在目标仓库 Settings → Secrets and variables → Actions 配 §2 的 secrets/vars。
3. 按仓库实际调整 `on.pull_request.paths`（哪些文件变更要触发门禁）。
4. 可选：在 Actions 页 `Run workflow` 手动触发并临时覆盖 base_url/dataset/workflow/threshold。
   注意：**fork PR 读不到 secrets**（GitHub 平台限制），`EVAL_GATE_BASE_URL` 为空 → 门禁步 fail-fast 报错阻断（预期内的 fail-closed）；如仓库接受外部 fork PR，建议仅对内部分支触发或将门禁改为 `repository_dispatch`/审批后运行。
5. 校验：提交前可本地 `python -c "import yaml,sys;yaml.safe_load(open('eval-gate.yml'))"`；正式由 actionlint/GitHub 校验。

## 4. GitLab CI 接入

方式 A（复制 job）：把 `eval-gate-gitlab.yml` 的 `eval-gate`（+ 可选 `notify-eval-gate-failure`）job 复制进目标 `.gitlab-ci.yml`，`stage` 对齐已有阶段。
方式 B（include）：把本文件提交到目标仓库某路径后：

```yaml
include:
  - local: 'ci/eval-gate-gitlab.yml'
```

在 Settings → CI/CD → Variables 配 §2 变量（勾 Masked，受保护分支场景勾 Protected）。用项目页 CI/CD → Pipeline Editor（CI Lint）校验。

---

## 5. 阈值策略

- 全局默认：服务端 `EvaluationSettings.GateMinPassRate`（appsettings 未配即 0.8）。
- 流水线覆盖：`EVAL_GATE_MIN_PASS_RATE` / workflow_dispatch 输入 → 请求体 `minPassRate`（优先级最高）。
- 建议：主干/重要 prompt 变更用较高阈值（如 0.9），探索分支用默认。空数据集恒判不通过（防「没测=通过」的假绿）。

## 6. 失败处理与通知

- 200 放行；其余一律 `exit 1` 阻断合并（门禁是硬闸门）。
- 失败通知：配了 Slack webhook 时，GitHub 在 `failure()` 步、GitLab 在 `.post` 的 `when: on_failure` job 发通知；通知失败不改变门禁结论。
- 排查产物：GitLab 在失败时以 `artifacts`（`gate-result.json`）保留门禁响应体；GitHub 直接 `cat` 到日志。

## 7. 故障排查

| 症状 | 可能原因 | 处理 |
|---|---|---|
| login 非 200 | 凭据错 / 实例不可达 / 角色问题 | 校验 base_url、账号密码；`401` 时先手动 curl 登录 |
| gate `403` | 账号非 Admin/Operator | 换服务账号或提权 |
| gate `404` | dataset/workflow GUID 错或跨租户不可见 | 核对 GUID、确认在数据集所属租户上下文 |
| gate `422` 但期望通过 | 阈值过高 / 模型退化 / 空集 | 看 `score/total`；降 `EVAL_GATE_MIN_PASS_RATE` 或修回归；空集先补用例 |
| gate `000` | 网络/防火墙/DNS | 从 runner 手动 `curl -I $BASE_URL/health` |
| 评估超时 | 数据集大 / 模型慢 | 评估请求内同步跑，调大 job `timeout`、控制用例数（≤ MaxCases）；长评估建议后续接 F37 队列化（当前门禁恒同步直跑，决策 D4） |
| 500（非预期码） | `minPassRate` 越界（服务端抛 `ArgumentOutOfRangeException`，**无 400 映射**→500）或其它服务端异常 | 传 0–1 之间的小数；模板已加本地数字校验，越界会在调用前即报错阻断 |

## 8. 与其他 feature 的关系

- **F34**：门禁端点本体（本模板是其 CI 侧消费方）。
- **F37 队列化执行**：门禁/评估为部署前阻塞语义，恒**同步直跑**（设计决策 D4），与队列后端无关；将来若门禁也入队，本模板以 HTTP 码为唯一契约不受影响。
- **F35/F36**：数据集与工作流受 workspace 隔离。注意解析优先级（`WorkspaceProvider.cs`）：**JWT `workspace_id` claim（优先级 1）压过 `X-Workspace-Id` 头（优先级 2）**，而登录时 claim 恒被写为**租户默认工作空间**（`AuthEndpoints.cs` 的 `GetDefaultAsync`）——门禁请求带 `X-Workspace-Id` 头**无法**把已含非空 claim 的会话切到其它 workspace（头仅在 claim 为空时兜底）。因此 CI 用的数据集/工作流必须位于**服务账号租户的默认 workspace**，否则门禁 `404`；多 workspace 场景需让目标 workspace 成为租户默认，或将回归数据集放入默认 workspace。租户维度同理：登录（匿名）可用 `X-Tenant-Id` 头定位非默认租户，JWT 签发后租户即固定。

## 9. 与 API schema 的同步义务

模板与 F34 端点逐字段对齐（路径 `POST /api/v1/evaluation-datasets/{id}/gate/{workflowId}`、body `minPassRate`、200/422 语义）。**若门禁端点契约变更（路径/方法/响应码/鉴权方式），必须同步更新本指南与 `ci/` 两模板**（F38 设计文档风险节已标注）。
