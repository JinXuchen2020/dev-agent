# 质量门报告：F14 · 供应商模型发现

> 阶段：`f14-model-discovery`
> 设计文档：`features/model-discovery.md`
> 提交：见 `feat/f14-model-discovery` gate（`.quality-gate.json` cleared=true）
> 日期：2026-07-28

## 1. 范围与交付

实现 `features/backlog.md` 中下一个 `open` epic —— **F14 · 供应商模型发现（填 Key + Base URL 后拉取可访问模型清单）**，🔴高风险（新增端点 + 鉴权 + 路由 + 前端契约），触发 feature-dev 高风险闸口（先设计后实现，设计文档即契约）。

用户在「我的凭据」/Agent 配置页填 **API Key + Base URL + 选 Provider** 后，点「拉取模型」即可从该 provider 账户（OpenAI 兼容 `GET /v1/models`）拉回所有可访问模型，以下拉供选择，免去手填模型名易错的问题。

**核心改动：**
- **后端发现服务**：`IProviderModelDiscovery`（Application.Abstractions 接口）+ `ProviderModelInfo` record + `ProviderModelDiscoveryException`（领域友好异常，携带可直接回传客户端的 400 中文原因，绝不泄露密钥）+ `ProviderModelDiscovery`（Infrastructure.Models，真实 `HttpClient` 出站，复用 `SerpApiSearchProvider` 的 `IHttpClientFactory` 模式，无 stub）。
- **端点**：`TenantCredentialsController` 新增 `POST discover-models`（RBAC `Admin,Operator`，只读探测、无落库、无密钥出 API 体）；`DiscoverModelsRequest`（Api.Models：provider / baseUrl / apiKey）。
- **DI 注册**：`IProviderModelDiscovery` 注册为 Scoped 单实现，控制器注入消费。
- **前端契约**：`types/index.ts` 加 `ProviderModelInfo`；`api.ts` 加 `discoverProviderModels`；`CredentialForm` 模型类 `Model Name` 改 `AutoComplete`（允许自定义）+「拉取模型」按钮（loading / 错误提示 / edit 模式留空 Key 禁用并用 `Tooltip` 提示先填 Key）。
- **无 EF 迁移**（D3：纯只读探测，无新聚合）。

## 2. 关键实现决策

- **默认 Base URL 解析**：`OpenAI`→`https://api.openai.com/v1`、`DeepSeek`→`https://api.deepseek.com/v1`；`Custom`/`VLLM` 必须显式填 `baseUrl`，缺失→400 中文提示。
- **超时与取消全链路**：请求级 `CancellationTokenSource(15s)` 与入参 `ct` 链接；**整段（连接 + 读取响应体 + 解析）都在 `try` 内**受超时保护，超时统一映射为友好 400 而非 500。
- **解析容错**：`JsonDocument` 遍历 `data[]`，`id` 必取、`owned_by` 容忍缺失；`data` 非数组 → 视为空并提示结构异常。
- **密钥安全**：`apiKey` 仅一次性用于出站 Bearer 头，不落库、不写日志、`ProviderModelInfo` 不含密钥；端点 `Admin,Operator` 门禁，SSRF 面 Admin 专用（D4 风险可接受）。
- **模型一致性**：后端 camelCase 序列化 `{id, ownedBy}`、前端对应 `{id, ownedBy}`，`tsc --noEmit` 0 error。

## 3. 质量门结果

| 维度 | 结果 |
|------|------|
| `dotnet build` | 0 警告 0 错误 |
| `dotnet test` | **255 passed / 0 failed**（SpecFlow 41 · Arch 6 · App 85 · Infra 102 · Api 16 · Integration 5）；F14 新增 11 例 |
| 前端 `tsc --noEmit` | 0 error |
| 前端 `vite build` | 通过 |
| `ddd-code-reviewer` | PASS（P0=P1=P2=P3=0，审查后） |
| `ddd-phase-quality-gate` | PASS（P0=P1=P2=P3=0；12 类审计全扫，checklist 已嵌 `features/model-discovery.md`） |
| `codebase-optimizer` | PASS（0 open；七维度扫描，无 stub / 空心实现） |

## 4. 新增测试（11 例 · `ProviderModelDiscoveryTests.cs`）

- OpenAI/DeepSeek 用默认 base、Custom 用 baseUrl → 正确拼接 `/models` 并解析 `id`/`owned_by`。
- VLLM 缺 baseUrl → 400；未知 provider → 400；空 key → 400。
- 401/403 → 中文「API Key 无效…」；404 → 中文「该端点不支持 /models…」。
- 空 `data` 数组、缺失 `data` 字段、缺失 `owned_by` → 容错处理。
- 用 `StubHttpMessageHandler` 验证真实 `GET` + `Bearer` 头 + URL，无 mock 短路。

## 5. 评审期修复

- **P1 — 超时读体 500→400**：原 `response.Content.ReadAsStringAsync` 位于 `try` 之外，若 15s 超时发生在读取响应体阶段会抛未捕获 `OperationCanceledException` → HTTP 500。已移入 `try` 并用 `using var response` 全程受请求级超时保护，超时统一映射为友好 400。
- **P2 — 禁用按钮提示不可见**：`CredentialForm`「拉取模型」按钮用 `title` 提示禁用原因，但 antd v5 中 disabled `Button` 吞掉 hover 事件导致提示不显示，违反 D1「按钮提示先填 Key」。已用 `Tooltip` 包裹，hover 正常显示。
- **P3 — 未用字段**：`ILogger` 注入后未使用，补一条不含密钥的信息日志（可观测性）。

## 6. 残留 / 已知限制（非阻塞）

- **e2e 浏览器联动（Playwright/Edge）**：本沙箱未跑端到端浏览器联动，单测已覆盖真实 HTTP 探测路径（StubHttpMessageHandler 验证 GET+Bearer+URL）。
- **SSRF 域名白名单**：不在本 feature 范围（D4），Admin 专用可接受；如需收紧后续加 provider 域名白名单。
- **部分非标准 OpenAI 兼容端点 `/models` 返回结构略有差异**：解析已容错（缺 `owned_by` 容忍、非数组 `data` → 视为空/错误提示）。

## 7. 文档与提交

- `features/model-discovery.md`：设计 + §Phase Quality Gate Checklist（12 类，已嵌入）。
- `features/backlog.md`：F14 标记为 done。
- `CHANGELOG.md`：新增 F14 完成记录（v2.7）。
- `.quality-gate.json`：推进 `f14-model-discovery`（cleared:true）。
- 提交含 `Quality-Gate:` 行，未使用 `--no-verify`，未 push。

**Gate Status: PASS**（P0=0 · P1=0 · P2=0 · P3=0）
