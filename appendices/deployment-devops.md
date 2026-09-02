## 附录 H：部署与 DevOps

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：平台包含多个服务（ASP.NET Core API、vLLM、PostgreSQL、Redis、Docker 沙箱），需要一套完整的部署方案避免"能编不能跑"的窘境。本附录覆盖从开发环境到生产部署的全链路。

### H.1 开发环境（docker-compose.yml）

```yaml
# docker-compose.dev.yml —— 一键启动全部依赖
version: "3.8"

services:
  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_USER: agent
      POSTGRES_PASSWORD: agent_dev
      POSTGRES_DB: agent_platform
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U agent"]
      interval: 5s

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  seq:
    image: datalust/seq:latest
    ports: ["5341:5341"]
    environment:
      ACCEPT_EULA: "Y"

  vllm:
    image: vllm/vllm-openai:latest
    ports: ["8000:8000"]
    # GPU 依赖，仅开发用小型模型
    command: --model Qwen/Qwen2.5-7B-Instruct --max-model-len 8192

volumes:
  pgdata:
```

> **开发启动**：`docker compose -f docker-compose.dev.yml up -d` → 启动后端基础设施 → `dotnet run --project src/AgentPlatform.Api` → 浏览器打开 `http://localhost:5000/swagger`

### H.2 生产部署架构

```
                                    ┌──────────────────────┐
                                    │   Nginx / Traefik    │
                                    │   (TLS 终结 + 反向代理)│
                                    └──────┬───────────────┘
                                           │
                 ┌─────────────────────────┼──────────────────────────┐
                 │                         │                          │
        ┌────────▼────────┐     ┌─────────▼──────────┐   ┌───────────▼──────────┐
        │  ASP.NET Core    │     │  ASP.NET Core       │   │  ASP.NET Core        │
        │  Web API (实例1) │     │  Web API (实例2)    │   │  Web API (实例3)     │
        │  :5000           │     │  :5001              │   │  :5002               │
        └────────┬────────┘     └─────────┬──────────┘   └───────────┬──────────┘
                 │                         │                          │
                 └──────────┬──────────────┴──────────────────────────┘
                            │
               ┌────────────▼──────────────────────────┐
               │          PostgreSQL (主从)              │
               │          Redis Cluster                 │
               │          Seq / Loki                    │
               │          Prometheus + Grafana          │
               └────────────────────────────────────────┘
```

**关键决策**：

| 决策项 | 选择 | 原因 |
| :--- | :--- | :--- |
| 反向代理 | **Nginx** 或 **Traefik** | TLS 终结 + 路径路由 + 健康检查 |
| 容器编排 | **Docker Compose**（初期）/ **Kubernetes**（规模 > 3 节点后） | 复杂度匹配规模 |
| 数据库 | PostgreSQL pgvector 主从 + Pgpool-II | 向量 + 结构化数据同库，简化运维 |
| 缓存 | Redis Cluster | 会话 + 短期记忆 + 消息队列 |
| 日志 | Seq（结构化日志）→ 长期存 Loki | 开发期用 Seq 最方便，生产换 Loki 省钱 |
| 监控 | Prometheus + Grafana | .NET 原生支持，配套完善 |
| GPU 服务 | vLLM 独立节点部署 | GPU 按需组件，不和 API Server 混部 |

### H.3 CI/CD 流水线（GitHub Actions）【阶段二实现】

```yaml
# .github/workflows/ci.yml
name: Agent Platform CI

on:
  push: { branches: [main, develop] }
  pull_request: { branches: [main] }

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: pgvector/pgvector:pg16
        env:
          POSTGRES_USER: test
          POSTGRES_PASSWORD: test
          POSTGRES_DB: agent_platform_test
        options: >-
          --health-cmd "pg_isready -U test"
          --health-interval 10s
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "9.0" }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release
      # BDD 验收
      - run: dotnet test tests/AgentPlatform.AcceptanceTests --no-build -c Release

  docker-build:
    needs: test
    steps:
      - uses: actions/checkout@v4
      - run: docker build -t agent-platform-api -f src/AgentPlatform.Api/Dockerfile .
      - run: docker tag agent-platform-api ghcr.io/${{ github.repository }}:latest
      # → 推送镜像到容器仓库...
```

### H.4 环境配置管理

```csharp
// appsettings.json — 开发环境
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=agent_platform;Username=agent;Password=agent_dev"
  },
  "Redis": { "Connection": "localhost:6379" },
  // F37 队列化执行：QueueEnabled=false 默认单实例直跑；生产多实例置 true + RedisStream/RabbitMQ
  "DurableExecution": { "QueueEnabled": false, "QueueBackend": "InMemory", "QueueMaxAttempts": 3 },
  "Jwt": { "Secret": "dev-secret-do-not-use-in-production" }
}

// appsettings.Production.json — 生产环境（CI/CD 注入，不入 Git）
{
  "ConnectionStrings": {
    "PostgreSQL": "__PG_CONNECTION_STRING__"        // 由 CI/CD Secret 替换
  },
  "Redis": { "Connection": "__REDIS_CONNECTION__" },
  "Jwt": { "Secret": "__JWT_SECRET__" },            // 256-bit 随机密钥
  "OpenTelemetry": {
    "Exporter": { "Endpoint": "http://otel-collector:4317" }
  }
}

// 使用 .NET 9 的 Aspire 或 dotnet user-secrets 管理本地 secret
// dotnet user-secrets set "Jwt:Secret" "local-jwt-key"
```

### H.5 扩容策略

| 场景 | 瓶颈 | 扩容措施 |
| :--- | :--- | :--- |
| API 吞吐不足 | ASP.NET Core CPU | 水平扩容：增加 API 实例（Nginx 负载均衡），无状态设计无需改代码 |
| 模型调用排队 | vLLM GPU | GPU 节点增加 → vLLM 接入 Nginx 上游组 → 路由层做负载均衡 |
| 数据库查询慢 | PostgreSQL | 主从分离：写走主库、读走从库；加 pgvector 索引优化向量检索 |
| 缓存命中低 | Redis | Redis Cluster 分片扩容；增加本地 MemoryCache 作为 L1 缓存 |
| 工作流并发高 | Agent 执行队列 | ✅ F37 已实现：`DurableExecution:QueueEnabled=true` + `QueueBackend=RedisStream`（或 RabbitMQ），多 `ExecutionWorker` 实例经消费组 + F30 租约水平扩展消费；无中间件时 InMemory 后端单实例回退 |

### H.6 前端发布

```
# Web 版：构建静态资源 → 部署到 CDN / Nginx
npm run build
scp -r dist/* user@server:/var/www/agent-platform/

# 桌面版：Tauri 构建原生安装包（CI/CD 自动构建）
npm run tauri build
# → 产物：my-platform_1.0.0_x64-setup.exe / my-platform_1.0.0_x64.dmg
# → 发布到 GitHub Releases 或企业内部分发系统
```

> **一句话总结**：本地开发 `docker compose up` 一键启动依赖，CI/CD 自动跑 BDD 验收 + 构建镜像（阶段二实现），生产部署按 Nginx + API Pool + PostgreSQL 主从 + Redis Cluster 标准架构水平扩容，前端双形态发布（Web 走 Nginx / 桌面走 Tauri CI 自动构建安装包）。
