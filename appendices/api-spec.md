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

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| POST | `/api/v1/auth/login` | 登录 | public |
| POST | `/api/v1/auth/refresh` | 刷新 Token | public |
| POST | `/api/v1/auth/logout` | 登出 | authenticated |
| GET | `/api/v1/auth/me` | 获取当前用户信息 | authenticated |

```json
// POST /api/v1/auth/login 请求
{
  "email": "user@example.com",
  "password": "********"
}

// 响应
{
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "dGhpcyBpcyBhIHJlZnJl...",
    "expiresAt": "2026-07-02T09:00:00Z",
    "user": {
      "id": "guid",
      "name": "张三",
      "email": "user@example.com",
      "role": "admin",
      "tenantId": "guid",
      "permissions": ["read:workflow", "write:workflow", "admin:tenant"]
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
| GET | `/api/v1/models` | 可用模型列表 | read:workflow |
| POST | `/api/v1/models/test` | 测试模型连通性 | admin |
| PUT | `/api/v1/models/{id}/priority` | 调整模型优先级 | admin |

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

### I.7 监控 API

| 方法 | 路径 | 说明 | 权限 |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/monitoring/metrics` | 实时指标快照 | admin |
| GET | `/api/v1/monitoring/logs` | 日志搜索 | admin |
| GET | `/api/v1/monitoring/alerts` | 告警历史 | admin |

> **一句话总结**：前端通过统一前缀 `/api/v1/` 的 REST API 与后端通信，7 个资源域（认证 / 工作流 / Agent / 模型 / 对话 / 管理 / 监控），对话流走 SSE 流式输出，权限按 RBAC 粒度控制。Agent 角色类型 API（`/agents/types`）支持动态加载自定义角色。完整 Swagger 文档在开发环境 `{host}/swagger` 实时生成。
