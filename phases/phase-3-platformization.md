# 阶段三：平台化与模型优化（2–3 周）

> 学习目标：从"能跑"到"好用"——后端 API 化、前端可视化、监控可观测。

## 学习目标

- [x] **ASP.NET Core Web API**：Controller 模式、中间件管道、Swagger/Scalar UI 集成
- [ ] **事件驱动的日志查询**：ExecutionLog 查询 API + SSE 进度推送
- [ ] **React 前端架构**：Vite 项目初始化、Ant Design 组件、React Router 路由、zustand 状态管理
- [ ] **React Flow**：节点 / 边 / 拖拽面板的自定义实现
- [ ] **OpenTelemetry 实战**：Metrics / Traces / Logs 三信号、Prometheus + Grafana 集成（含蓝图 §8.2 `AppMetrics` Counter/Histogram 实现）
- [ ] **CI/CD 入门**：GitHub Actions 配置、自动构建 + 测试
- [ ] **性能基准测试**：压力测试工具、P50/P95/P99 理解、性能调优方法论

## 前置依赖

- [ ] 阶段二已完成并提交
- [ ] 阶段二的 BDD 验收全部通过
- [ ] React + TypeScript 开发环境已就绪（Node.js >= 18）

## 任务清单

- [x] **后端服务**：ASP.NET Core Web API，启用 Swagger 接口文档（**知识点**：REST API 设计 + 文档化）
- [ ] **Agent 配置模块**：YamlDotNet 解析配置 + EF Core 持久化 + 版本管理（**知识点**：配置管理 + 版本化）
- [ ] **工作流编排模块**：基于自研状态机，**React Flow** 拖拽可视化配置（**知识点**：前端工作流编辑器）
- [ ] **前端**：**React** (TypeScript + Vite + Ant Design)，通过 REST API 对接后端（**知识点**：全栈联调）
- [ ] **监控**：OpenTelemetry 接入 Prometheus + Grafana（**知识点**：可观测性三支柱）
- [ ] **自定义 AgentType 后端**：种子数据 + 租户级 CRUD API（**知识点**：多租户数据设计）
- [ ] **前端角色面板**：预置 + 自定义角色分区展示，API 动态加载（**知识点**：前端动态数据加载）
- [ ] **性能基准验证**：单租户并发 5 工作流、步骤 P95 延迟 < 30s（**知识点**：压测 + 瓶颈定位）
- [ ] **ExecutionLog 查询 API**：实现 4 个端点（列表 / 步骤 / 详情 / 错误筛选）
- [ ] **SSE 进度推送**：状态机执行时推送 `step_progress` 事件到前端
- [ ] **前端进度面板**：步骤列表中实时展示当前步骤状态和进度条
- [ ] **日志清理 Job**：定时删除 90 天前的日志、清理 30 天前的 payload

## 验收标准

1. 平台可以通过 Web 界面拖拽编排工作流
2. 前端预置和自定义角色分区展示，角色从 API 动态加载
3. Grafana 大盘实时展示 8.1 定义的全部指标
4. 性能基准：单租户并发 5 工作流、步骤 P95 < 30s
5. CI 自动构建 + 跑全量测试

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：
- **完成日期**：
- **完成度**：█░░░░░░░░░ 0%

## 回顾（完成后填写）

### 做得好的

### 下次改进

### 对蓝图文档的反馈
