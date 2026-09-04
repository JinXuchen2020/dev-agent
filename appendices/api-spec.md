## 附录 I：API 接口规范

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：前端通过 REST API + WebSocket 与后端通信，本附录枚举关键 API 路径的请求/响应结构和约定。完整 OpenAPI 规范由 Swagger (Scalar UI) 运行时生成，此处只列出核心接口。

### I.1 路由前缀与约定

```
前缀：/api/v1/{resource}
通用响应格式：
{
  "data": { ... },          // 成功时返回
  "error": {                // 失败时返回
    "code": "WORKFLOW_NOT_FOUND",
    "message": "工作流未找到"
  },
  "meta": {                 // 可选，分页信息
    "page": 1,
    "pageSize": 20,
    "total": 156
  }
}
```

### I.2 认证 API

> **传输方式**：登录成功后 JWT 写入 **`ap_access_token` httpOnly Cookie**（SameSite=Lax、HTTPS 下 Secure、MaxAge=1h）。前端 `axios` 设 `withCredentials: true`，**不在 JS / localStorage 存 token**；`AuthConfiguration` 的 Smart 策略从 cookie 读 JWT 完成鉴权。登出即删 cookie。

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/auth/login` | 登录（邮箱+密码，PBKDF2 校验），成功设 cookie | public |
| GET | `/api/v1/auth/me` | 获取当前用户信息（从 cookie 解析 ClaimsPrincipal） | authenticated |
| POST | `/api/v1/auth/logout` | 登出（删除 cookie） | authenticated |
| POST | `/api/dev/login` | 开发登录（仅 `DevLoginEnabled=true` 时启用，默认 false），返回裸 JWT 供 Swagger/Scalar 调试 | public |

> 说明：**无 Refresh Token 端点**——cookie 1h 过期后重新登录即可。前端另提供「本地演示会话」路径（不请求后端、不写 cookie，`isDemo` 跳过 401 跳转），用于纯前端演示。

```json
// POST /api/v1/auth/login 请求
{
  "email": "admin@acme.io",
  "password": "Admin@123456"
}

// 响应（无 token 字段，身份由 cookie 携带）
{
  "data": {
    "user": {
      "id": "guid",
      "email": "admin@acme.io",
      "role": "Admin",
      "tenantId": "guid"
    }
  }
}
```

### I.3 工作流 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/workflows` | 工作流列表（分页） | read:workflow |
| POST | `/api/v1/workflows` | 创建工作流 | write:workflow |
| GET | `/api/v1/workflows/{id}` | 工作流详情 | read:workflow |
| PUT | `/api/v1/workflows/{id}` | 更新工作流 | write:workflow |
| DELETE | `/api/v1/workflows/{id}` | 删除工作流 | write:workflow |
| POST | `/api/v1/workflows/{id}/execute` | 执行工作流（异步） | write:workflow |
| GET | `/api/v1/workflows/{id}/executions` | 执行历史 | read:workflow |
| GET | `/api/v1/workflows/{id}/stream` | 流式执行状态（SSE） | read:workflow |
| POST | `/api/v1/workflows/{id}/publish` | 发布为 API/MCP（body: `mode` Api\|Mcp, `apiKeyId?`, `inputSchema?`） | write:workflow |
| DELETE | `/api/v1/workflows/{id}/publish` | 取消发布（幂等，未发布无操作） | write:workflow |
| GET | `/api/v1/workflows/{id}/publish` | 发布状态（未发布 → 204） | read:workflow |
| POST | `/api/v1/workflows/{id}/triggers/webhook` | 生成/启用 Webhook 令牌（幂等复用现有令牌） | write:workflow |
| DELETE | `/api/v1/workflows/{id}/triggers/webhook` | 禁用 Webhook（令牌保留但失效） | write:workflow |
| PUT | `/api/v1/workflows/{id}/triggers/schedule` | 启用/更新/禁用 Schedule（cron+时区，幂等 upsert） | write:workflow |
| GET | `/api/v1/workflows/{id}/triggers` | 查询触发器配置（Webhook/Schedule/Chat 绑定数） | read:workflow |

```json
// POST /api/v1/workflows 请求
{
  "name": "代码生成流水线",
  "description": "从需求到文档的完整开发流水线",
  "steps": [
    {
      "order": 1,
      "agentId": "guid-1",
      "type": "sequential",
      "systemPrompt": "你是一名资深需求分析师...",
      "timeoutSeconds": 120,
      "retryCount": 2
    },
    {
      "order": 2,
      "agentId": "guid-2",
      "type": "sequential",
      "timeoutSeconds": 300
    }
  ],
  "connections": [
    { "from": "step-1", "to": "step-2", "label": "success" }
  ]
}

// GET /api/v1/workflows/{id} 响应
{
  "data": {
    "id": "guid",
    "name": "代码生成流水线",
    "status": "idle | running | completed | failed",
    "steps": [ /* ... */ ],
    "currentStepIndex": null,
    "createdAt": "2026-06-30T10:00:00Z",
    "updatedAt": "2026-06-30T14:30:00Z"
  }
}
```

### I.3.1 工作流运行与队列模式（F37）

运行端点 `POST /api/v1/workflows`（创建并运行）与 `POST /api/v1/workflows/{id}/run`（重跑）在两种模式下响应不同：

- **直跑模式（`DurableExecution:QueueEnabled=false`，默认）**：请求内同步跑完编排，返回完整工作流聚合（200）。
- **队列模式（=true）**：请求先入队 → 服务端在等待窗口（`QueueWaitTimeoutSeconds`，默认 110s < 前端超时）内轮询终态。三态：
  - **200**：等待窗口内到达终态/暂停，body = 工作流聚合（与直跑同构）。
  - **202**：`{queued:true, workflowId, state}` —— 未及终态，执行仍由 worker 继续，进度经 SSE/详情查看。
  - **503**：入队被拒（队列满 / 后端不可用），**绝不静默丢任务**。
- 三后端（`QueueBackend`）：`InMemory`（默认，进程内 Channel 有界）/ `RedisStream`（消费组 + XAUTOCLAIM 崩溃接管）/ `RabbitMQ`（durable 队列 + BasicGet pull + 断线重投）。至少一次投递，重复消费由 F30 `RunningExecution` 租约互斥兜底。
- 触发器（Webhook/Schedule）在队列模式下改投递队列由 worker 执行；入队失败降级直跑（记 warning）。评估门禁 F34 保持同步直跑（决策 D4）。

### I.3.2 发布工作流外部调用 API（F22）

> 由 `POST /api/v1/workflows/{id}/publish` 生成的对外能力。鉴权复用现有 **API Key** 体系（`[Authorize(AuthenticationSchemes="ApiKey")]` + `PerApiKey` 令牌桶限流，非 JWT/cookie），租户由密钥 `tenant_id` 声明自动解析，`key_id` 声明用于调用审计归属。

| 方法 | 路径 | 说明 | 鉴权 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/published-workflows/{slug}` | 按 slug 运行已发布的 **API 模式**工作流（body: `inputJson?`） | API Key |
| POST | `/api/v1/mcp` | 平台内 **MCP** 暴露端点（JSON-RPC 2.0：`tools/list` + `tools/call`） | API Key |

**运行端点约定：**
- 无效 / 禁用 / 绑定 Key 不匹配的 slug → **404**（不泄露存在性）。
- 输入若定义了 `InputSchemaJson` 且含 `required`，缺字段 → **400**。
- 工作流处于 `Running` → **409**（Conflict）。
- 终态/暂停态重跑：服务端先重置为干净状态再运行（同 F7 重跑语义）。
- 返回仅最终输出（`status` + `output`），不含中间节点 Trace（Trace 视图见 F24）。

```json
// POST /api/v1/published-workflows/{slug} 请求
{ "inputJson": "{\"topic\":\"Q3 复盘\"}" }

// 响应 200
{
  "workflowId": "guid",
  "slug": "aZ3kPq9XyLmN2bRt",
  "status": "Completed",
  "output": "{...最终 blackboard JSON...}",
  "errorMessage": null
}
```

```json
// POST /api/v1/mcp  （JSON-RPC 2.0）
// tools/list 请求
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {} }
// tools/list 响应（仅 Enabled && Mode==Mcp 的发布记录，name/description 同取 slug）
{ "jsonrpc": "2.0", "id": 1,
  "result": { "tools": [ { "name": "aZ3kPq9XyLmN2bRt", "description": "aZ3kPq9XyLmN2bRt",
                           "inputSchema": { "type": "object" } } ] } }

// tools/call 请求（name = slug）
{ "jsonrpc": "2.0", "id": 2, "method": "tools/call",
  "params": { "name": "aZ3kPq9XyLmN2bRt", "arguments": { "topic": "Q3 复盘" } } }
// tools/call 响应（执行异常按 MCP 约定 isError=true，不抛 HTTP 错误）
{ "jsonrpc": "2.0", "id": 2,
  "result": { "content": [ { "type": "text", "text": "{...输出...}" } ], "isError": false } }
```
### I.3.1 匿名 Webhook 入口（F21 工作流触发器）

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/webhooks/workflow/{token}` | 凭令牌触发绑定工作流（请求体原样作触发载荷）；token 不存在或禁用→404 | 匿名（受 `WebhookAnonymous` 限流） |

> 令牌即鉴权，不依赖 JWT/Cookie；未知或禁用令牌统一返回 404，不泄露工作流存在性。

### I.4 Agent API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/agents` | Agent 列表（支持按角色筛选） | read:workflow |
| POST | `/api/v1/agents` | 创建 Agent | write:workflow |
| GET | `/api/v1/agents/{id}` | Agent 详情 | read:workflow |
| PUT | `/api/v1/agents/{id}` | 更新 Agent | write:workflow |
| DELETE | `/api/v1/agents/{id}` | 删除 Agent | write:workflow |
| GET | `/api/v1/agents/types` | Agent 角色类型列表（自定义 + 预置） | read:workflow |
| POST | `/api/v1/agents/types` | 创建自定义角色类型 | admin |

```json
// GET /api/v1/agents/types 响应
{
  "data": [
    { "code": "REQ", "displayName": "需求分析师", "description": "...", "icon": "clipboard", "isPreset": true },
    { "code": "DEV", "displayName": "开发工程师", "description": "...", "icon": "code", "isPreset": true },
    { "code": "DEVOPS", "displayName": "DevOps 工程师", "description": "...", "icon": "rocket", "isPreset": false }
  ]
}
```

### I.5 模型 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/models` | 可用模型列表（平台内置 ∪ 租户 BYO，仅返回 `modelId/provider/displayName`，**不含密钥**） | authenticated |
| POST | `/api/v1/models/test` | 测试模型连通性 | admin |
| PUT | `/api/v1/models/{id}/priority` | 调整模型优先级 | admin |

### I.5.1 多租户凭据 API（F13）

多租户外部 API 凭据（模型 LLM key + 搜索 SerpApi key）的 BYO-Key 配置与平台内置回退。密钥属高敏，仅 `Admin/Operator` 可写；所有响应**绝不**返回明文密钥（掩码 `••••`+prefix）。

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/tenant/credentials?category=Model\|Search` | 获取当前租户某类凭据；未配置返回 `204`；返回 `TenantCredentialDto`（`apiKeyMask` 掩码，无明文） | Admin,Operator |
| PUT | `/api/v1/tenant/credentials` | 创建/覆盖更新（upsert，按 `tenantId+category`）；入站明文 `apiKey` 加密后立即丢弃，留空则沿用既有密文；成功后使该租户+类别解析缓存失效 | Admin,Operator |
| POST | `/api/v1/tenant/credentials/discover-models` | **（F14）** 供应商模型发现：填 Key+Base URL 后从该 provider 账户（`GET {base}/models`，OpenAI 兼容）拉回可访问模型清单；只读探测，密钥不落库不记日志；`baseUrl` 对 OpenAI/DeepSeek 可省略（用内置默认） | Admin,Operator |

```jsonc
// PUT /api/v1/tenant/credentials 请求
{
  "category": 0,            // 0=Model, 1=Search
  "provider": "DeepSeek",   // 模型: OpenAI/DeepSeek/VLLM/Custom；搜索: SerpApi
  "apiKey": "sk-...",       // 明文，仅入站，服务端加密后丢弃；首次必填，更新可留空
  "baseUrl": "https://api.deepseek.com", // 模型端点/自定义 OpenAI 兼容 base；搜索通常留空
  "modelName": "deepseek-chat",          // 仅模型类
  "isEnabled": true
}

// GET /api/v1/tenant/credentials?category=Model 响应（已配置）
{ "category": 0, "provider": "DeepSeek", "apiKeyMask": "••••sk-ABCD1234", "baseUrl": "https://api.deepseek.com", "modelName": "deepseek-chat", "isEnabled": true }

// POST /api/v1/tenant/credentials/discover-models 请求（F14）
{ "provider": "DeepSeek", "apiKey": "sk-...", "baseUrl": "https://api.deepseek.com" }  // baseUrl 对 OpenAI/DeepSeek 可省略

// 响应 200（ProviderModelInfo[]）
[ { "id": "deepseek-chat", "ownedBy": "deepseek" }, { "id": "deepseek-reasoner", "ownedBy": "deepseek" } ]

// 失败 400（密钥无效/无权限/端点不支持/超时/传输错误，返回中文原因，不泄露密钥）
{ "title": "Bad Request", "status": 400, "detail": "API Key 无效或无权访问该 provider 的模型列表" }
```

### I.11 工作空间 API（F35）

同租户内第二层隔离维度。决策 D1=C：JWT `workspace_id` claim 默认 + `X-Workspace-Id` header 覆盖（服务端
`WorkspaceHeaderGuardMiddleware` 对非 Admin 剥离不可见的头）；决策 D3=B：非 Admin 仅可见/可切默认空间与自己已加入的空间。

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/workspaces` | 列出对调用者可见的工作空间（Admin 全部；非 Admin = 默认 + 已加入） | authenticated |
| POST | `/api/v1/workspaces` | 新建 `{name, description?}`；重名 409 | Admin |
| PUT | `/api/v1/workspaces/{id}` | 重命名/改描述；不存在 404；重名 409 | Admin |
| DELETE | `/api/v1/workspaces/{id}` | 删除空的非默认工作空间；默认空间 409；仍含成员/业务实体 409（绝不级联） | Admin |
| GET | `/api/v1/workspaces/{id}/members` | 成员列表（`[{userId,email,joinedAt}]`） | Admin |
| POST | `/api/v1/workspaces/{id}/members` | 按邮箱添加成员 `{email}`；用户不存在 404；已是成员 409 | Admin |
| DELETE | `/api/v1/workspaces/{id}/members/{userId}` | 移除成员 | Admin |
| POST | `/api/v1/workspaces/{id}/switch` | 切换：校验可见性后重签 JWT（`workspace_id` claim）并刷新 httpOnly cookie；不可见 404 | authenticated |

```jsonc
// GET /api/v1/workspaces 响应（WorkspaceDto[]）
[ { "id": "guid", "name": "Default", "description": null, "isDefault": true, "createdAt": "2026-08-31T00:00:00Z" } ]

// POST /{id}/switch 响应
{ "workspace": { "id": "guid", "name": "W1", "isDefault": false, "...": "..." }, "token": "eyJ..." }
```

### I.6 对话 API（SSE 流式）

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/conversations` | 会话列表；可选 `?agentId={guid}` 按归属 agent 过滤（F36，agent 步骤自动建的 per-agent 会话；不传=全部含全局会话） | read:workflow |
| POST | `/api/v1/conversations` | 创建会话 | write:workflow |
| GET | `/api/v1/conversations/{id}` | 会话详情（含消息历史） | read:workflow |
| POST | `/api/v1/conversations/{id}/messages` | 发送消息（流式响应 via SSE） | write:workflow |
| DELETE | `/api/v1/conversations/{id}` | 删除会话 | write:workflow |
| GET | `/api/v1/conversations/{id}/workflow-bindings` | 列出会话绑定的工作流（Chat 触发器） | read:workflow |
| POST | `/api/v1/conversations/{id}/workflow-bindings` | 绑定会话到工作流（幂等，双重租户校验） | write:workflow |
| DELETE | `/api/v1/conversations/{id}/workflow-bindings/{workflowId}` | 解绑工作流 | write:workflow |
| POST | `/api/v1/conversations/{id}/trigger-workflow/{workflowId}` | 会话上下文触发绑定工作流（未绑定→404） | write:workflow |

```typescript
// SSE 流式响应格式
// POST /api/v1/conversations/{id}/messages
// 请求
{ "content": "给我生成一个用户登录模块", "model": "gpt-4o" }

// 响应（text/event-stream）
event: token
data: {"type": "token", "content": "好的"}

event: token
data: {"type": "token", "content": "，我来为你设计用户登录模块。"}

event: step_start
data: {"type": "step_start", "stepIndex": 1, "agentName": "需求分析师"}

event: error
data: {"type": "error", "code": "MODEL_TIMEOUT", "message": "模型响应超时"}

event: done
data: {"type": "done", "executionId": "guid"}
```

### I.7 调研 API（SSE 流式）

> Research Agent：开放问题 → 多步联网检索（真实 SerpAPI HTTP）→ 结构化报告。详见 `features/research-agent.md`。

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/research` | 提交调研问题，SSE 流式返回多步进度与最终报告 | authenticated |

```typescript
// 请求
{ "question": "2025 年大模型推理成本下降趋势及主要驱动因素", "maxSteps": 3, "focusInstructions": null, "modelId": null }

// 响应（text/event-stream，每个 data 帧为一个 ResearchProgressEvent）
// type 为整型枚举：0=Plan 1=SearchStart 2=SearchDone 3=Synthesize 4=Report 5=Error
event: data
data: {"type":0,"queries":["趋势概览","驱动因素"],"message":"已规划 2 个检索查询"}

event: data
data: {"type":1,"query":"趋势概览","message":"检索中：趋势概览"}

event: data
data: {"type":2,"query":"趋势概览","snippetCount":5,"message":"检索完成：趋势概览（5 条）"}

event: data
data: {"type":4,"report":{"question":"...","searchQueries":["..."],"sources":[{"title":"...","url":"...","snippet":"..."}],"answer":"...","sections":[{"heading":"结论","body":"..."}],"stepsUsed":2,"tokenUsage":{"promptTokens":1200,"completionTokens":800},"generatedAt":"2026-07-24T..."}}

event: done
data: {}
```

> 说明：前端以 `fetch` + `credentials:'include'` 消费（EventSource 仅支持 GET）；终端帧为 `event: done` 空 `data: {}`。搜索密钥经 `Search:SerpApiKey` 配置 / 环境变量 `Search__SerpApiKey`，**不落库**；缺 key / 非 2xx / 超时 → 对应查询 `SearchDone` 事件回打精准错误，报告仍基于已规划内容生成。

### I.8 监控 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/monitoring/metrics` | 实时指标快照 | admin |
| GET | `/api/v1/monitoring/logs` | 日志搜索 | admin |
| GET | `/api/v1/monitoring/alerts` | 告警历史 | admin |

### I.9 模板市场 API（F23）

> **用途**：平台级工作流模板（随种子落地，对所有租户共享、只读）。认证用户可浏览 / 预览；克隆为「我的工作流」需 Admin / Operator。

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/workflow-templates` | 列出模板，支持 `?category=`（`WorkflowTemplateCategory` 数值）与 `?keyword=` 过滤 | authenticated |
| GET | `/api/v1/workflow-templates/categories` | 返回全部分类选项（`[{value,name}]`，8 项） | authenticated |
| GET | `/api/v1/workflow-templates/{id:guid}` | 模板详情（含预览图 `context` / `nodes` / `edges`） | authenticated |
| POST | `/api/v1/workflow-templates/{id:guid}/clone` | 克隆为当前租户的新 `Workflow`（Agent 全解绑、审计 `CloneTemplate`） | Admin, Operator |

```json
// GET /api/v1/workflow-templates 响应（WorkflowTemplateSummaryResponse[]）
[
  {
    "id": "22222222-2222-2222-2222-222222222201",
    "name": "知识库智能问答",
    "category": 1,                 // WorkflowTemplateCategory.KnowledgeQa
    "description": "…",
    "tags": ["rag", "qa"]
  }
]

// GET /api/v1/workflow-templates/{id} 响应（WorkflowTemplateDetailResponse，节选）
{
  "id": "guid", "name": "…", "category": 1, "description": "…", "tags": ["…"],
  "context": "系统提示词…",
  "nodes": [{ "id": "guid", "name": "开始", "type": 0, "agentId": null, "configJson": "{}" }],
  "edges": [{ "id": "guid", "source": "nodeA", "target": "nodeB" }]
}

// POST /api/v1/workflow-templates/{id}/clone 响应（WorkflowDetailResponse）
// 成功 200；模板不存在 404；非 Admin/Operator 403
```

> 注：模板为平台级共享资源，`WorkflowTemplate` **不实现** `ITenantScoped`（不受租户查询过滤器约束，决策 S2）；克隆出的 `Workflow` 才带当前租户。`ListAsync` 的 `keyword` 走 `EF.Functions.Like` 参数化（无 SQL 注入）。

### I.10 评估 API（F24 · 执行 Trace / 评估视图）

> **用途**：节点级可观测性（Trace）与数据集回归评估。Trace 基于既有 `ExecutionLog`（通用、不依赖特定 `StepType`）；评估为租户隔离的数据集回归，对标 LangSmith / Langfuse。

#### I.10.1 执行日志 Trace 字段扩展（F24）

既有 `GET /api/v1/execution-logs/{id}` 与 `GET /api/v1/execution-logs/{id}/steps` 的 `entries[]` 每项新增三字段（仅扩响应模型，不改路由与鉴权）：

```json
// ExecutionLogStepEntry 增量字段
{
  "tokensIn": 128,        // 该节点输入 token（复用模型层已算 TokenUsage）
  "tokensOut": 42,        // 该节点输出 token
  "nodeType": 0           // StepType?：0=Start,1=End,2=Agent,3=Condition,4=Loop,5=Tool,6=Manual,7=Parallel,8=SubWorkflow,9=Message,10=Transform（其余节点类型通用）
}
```

> 注：`ExecutionLog` 不实现 `ITenantScoped`，故租户隔离必须由查询侧显式施加（见 I.10.3 租户收口）；
> **节点级 Input（入参）不落库**（平台无该字段，回放面板的「输入」为前序输出推断值，见 I.10.3）。

#### I.10.3 异常回放诊断与租户收口（F40）

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/execution-logs/{id:guid}/replay` | **只读**从执行日志条目重建异常路径：节点序列 + 失败判定 + 前后上下文 + 末次 Blackboard 快照 + `dataGaps`。不重新执行任何步骤、不写任何状态 | Admin,Operator |

```jsonc
// POST /api/v1/execution-logs/{id}/replay → 200 ReplayReport（节选）
{
  "overallStatus": 4,                     // WorkflowState 数值（无 JsonStringEnumConverter）
  "nodes": [ {
      "stepOrder": 2, "stepName": "Review Step", "status": 4, "nodeType": 4, "isFailure": true,
      "input": "draft output", "inputInferred": true,   // 真实入参未落库 → 推断值必须显式标注
      "output": null, "outputLength": 0, "outputTruncated": false,
      "errorDetail": "模型返回超限", "errorTruncated": false,
      "tokensIn": 0, "tokensOut": 0, "tokensReported": false } ],
  "failurePath": { "firstFailedStepOrder": 2, "failedStepNames": ["Review Step"], "failedCount": 1 },
  "contextSnapshot": {
      "available": true, "source": "F30-final-checkpoint",
      "variables": { "loop.x": "1" }, "checkpointVersion": 2, "executionOrderIndex": 2,
      "stepStateCount": 0,
      "note": "末次检查点快照（F30 覆盖写，非 per-step 历史）…" },
  "recordedStepCount": 3, "missingStepCount": 0,
  "dataGaps": ["input-snapshot-unavailable", "total-steps-unregistered", "tokens-not-reported"]
}
```

`dataGaps` 稳定码（前端据此灰显并提示，避免把「信息缺失」读成「没有问题」）：
`input-snapshot-unavailable`（真实入参未落库）、`node-type-missing-legacy-rows`（F24 前旧行无 NodeType）、
`tokens-not-reported`、`context-snapshot-unavailable`、`context-snapshot-unparsable`（检查点损坏/格式演进）、
`steps-missing-truncated-execution`、`total-steps-unregistered`（建档时 `TotalSteps` 未知恒 0 → 缺步数不可判）、
`report-nodes-capped`（响应封顶 `MaxNodesInReport=500`）。

能力边界与防护：① F30 只保留**末次**检查点，不声称可回放每一步上下文（`contextSnapshot.note` 明示）；
② 长文本截断 4000 字符且**代理对安全**（撕裂会产生 U+FFFD 篡改诊断文本），原始长度经 `outputLength` 回传；
③ 不存在或跨租户 → 404（不暴露存在性）。

> **租户收口（同批安全修复）**：仓储 `GetByIdAsync` 不带租户谓词，而 `ExecutionLog` 又不在全局 query filter 覆盖范围内 —— 既有 `GET /{id}`、`GET /{id}/steps` 存在「持 GUID 即可读他租户日志」的窗口（**F40 之前就存在**）。现三个读端点统一改用 `GetByIdForTenantAsync` / `IsOwnedByTenantAsync`（跨租户与不存在同为 404），并有 EF 级测试实证无过滤路径可跨租户取数以防回归。

#### I.10.2 评估数据集 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/evaluation-datasets` | 列出当前租户数据集，支持 `?keyword=` 过滤 | authenticated |
| GET | `/api/v1/evaluation-datasets/{id:guid}` | 数据集详情（含 `cases[]`） | authenticated |
| POST | `/api/v1/evaluation-datasets` | 新建数据集（body：`name, description?, cases[]`） | Admin, Operator |
| PUT | `/api/v1/evaluation-datasets/{id:guid}` | 改名 / 描述 / 替换 `cases[]`（PUT 语义） | Admin, Operator |
| DELETE | `/api/v1/evaluation-datasets/{id:guid}` | 删除（tenant-scoped 级联删 cases） | Admin, Operator |
| POST | `/api/v1/evaluation-datasets/{id:guid}/run` | 对该数据集跑评估（body：`{ workflowId }`）→ `EvaluationReport` | Admin, Operator |

```json
// POST /api/v1/evaluation-datasets 请求
{
  "name": "客服意图回归集",
  "description": "可选",
  "cases": [
    { "input": "我要退款", "expectedOutput": "已为您发起退款", "matchMode": 1 }  // 0=Exact,1=Contains
  ]
}

// GET /api/v1/evaluation-datasets/{id} 响应（EvaluationDatasetDetailResponse）
{
  "id": "guid", "name": "客服意图回归集", "description": "可选",
  "cases": [
    { "id": "guid", "input": "我要退款", "expectedOutput": "已为您发起退款", "matchMode": 1 }
  ],
  "createdAt": "2026-08-05T10:00:00Z"
}

// POST /api/v1/evaluation-datasets/{id}/run 响应（EvaluationReport）
{
  "total": 3, "passed": 2, "score": 0.6667,
  "cases": [
    {
      "input": "我要退款", "expectedOutput": "已为您发起退款", "actualOutput": "已为您发起退款流程",
      "passed": true, "durationMs": 1820, "tokensIn": 128, "tokensOut": 42, "errorDetail": null
    }
  ]
}
```

> 注：`EvaluationDataset` 实现 `ITenantScoped`（自动全局过滤）；`RunEvaluation` 每 case 克隆全新 `Workflow`（new Guid）避免污染源工作流，逐 case 复用编排器 step 超时 bounding，硬上限 `EvaluationSettings.MaxCases`（默认 10，可配）；`matchMode`：`Exact=string.Equals(Ordinal)` / `Contains=actual.Contains(expected, OrdinalIgnoreCase)`；缺失 dataset / workflow → 404。

> **一句话总结**：前端通过统一前缀 `/api/v1/` 的 REST API 与后端通信，11 个资源域（认证 / 工作流 / 模板市场 / Agent / 模型 / 对话 / 调研 / 管理 / 监控 / 评估 / 工作空间），对话与调研流均走 SSE 流式输出，权限按 RBAC 粒度控制。Agent 角色类型 API（`/agents/types`）支持动态加载自定义角色。完整 Swagger 文档在开发环境 `{host}/swagger` 实时生成。
