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
```

### I.6 对话 API（SSE 流式）

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/conversations` | 会话列表 | read:workflow |
| POST | `/api/v1/conversations` | 创建会话 | write:workflow |
| GET | `/api/v1/conversations/{id}` | 会话详情（含消息历史） | read:workflow |
| POST | `/api/v1/conversations/{id}/messages` | 发送消息（流式响应 via SSE） | write:workflow |
| DELETE | `/api/v1/conversations/{id}` | 删除会话 | write:workflow |

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

> **一句话总结**：前端通过统一前缀 `/api/v1/` 的 REST API 与后端通信，8 个资源域（认证 / 工作流 / Agent / 模型 / 对话 / 调研 / 管理 / 监控），对话与调研流均走 SSE 流式输出，权限按 RBAC 粒度控制。Agent 角色类型 API（`/agents/types`）支持动态加载自定义角色。完整 Swagger 文档在开发环境 `{host}/swagger` 实时生成。
