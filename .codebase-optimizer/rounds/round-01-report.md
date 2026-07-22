# Round 1: 阶段1 基础质量分析报告

**分析时间**: 2026-07-22
**当前阶段**: 阶段1（基础质量）
**分析维度**: 架构 → 代码质量 → 正确性 → 测试
**分析范围**: 全项目（src/ 下的 239 .cs + 33 .ts/.tsx）

## 发现的问题

### 1. [P1] OrchestrationPrimitive 上帝类（636 行）
- **维度**: 🏗️ 架构
- **文件**: `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs:1-636`
- **描述**: 一个类承担了顺序编排、协商编排、步骤重试、上下文构建、预设检测、通配符匹配等职责。静态 ConcurrentDictionary 管理运行中工作流（可能的内存泄露）。
- **建议**: 拆分为 SequentialOrchestrator / NegotiationOrchestrator；将 s_runningCts/s_resolvedPresets 替换为持久存储或 TTL 驱逐。
- **状态**: ⏳ 待处理

### 2. [P1] Program.cs 臃肿（348 行）+ dev-login 端点内联
- **维度**: 🏗️ 架构
- **文件**: `src/AgentPlatform.Api/Program.cs:1-348`
- **描述**: DI 注册、中间件管道、认证配置、dev-login 路由全部在一个文件。内联的 dev-login 端点使用了硬编码的 JWT 回退密钥。
- **建议**: 提取为 WebApplicationExtensions / DevLoginEndpoint 独立类；JWT 密钥缺失应在启动时快速失败。
- **状态**: ⏳ 待处理

### 3. [P2] API 无版本控制
- **维度**: 🏗️ 架构
- **文件**: 全局
- **描述**: 蓝图附录 I 使用 `/api/v1/` 前缀但无版本控制策略；路由硬编码，不利于向后兼容演进。
- **建议**: 引入 Asp.Versioning.Mvc 并制定弃用策略。
- **状态**: ⏳ 待处理

### 4. [P2] AgentPlatform.Workflow 项目为空骨架
- **维度**: 🏗️ 架构
- **文件**: `src/AgentPlatform.Workflow/` 目录
- **描述**: 蓝图规划为独立工作流引擎项目，但始终是空目录。实际工作流引擎实现在 Infrastructure/Workflows/ 中，造成两处混淆。
- **建议**: 填充或移除该项目。如果保留，至少添加 README 说明用途。
- **状态**: ⏳ 待处理

### 5. [P2] Truncate 方法重复 3 处
- **维度**: 🧹 代码质量
- **文件**: 
  - `src/AgentPlatform.Infrastructure/Workflows/AgentCallStepExecutor.cs:106-108`
  - `src/AgentPlatform.Infrastructure/Workflows/CriticStepExecutor.cs:180-182`
  - `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs:567-568`
- **描述**: 完全相同的 `Truncate(string, int)` 私有方法在三处重复定义。
- **建议**: 提取到共享的 Infrastructure/StringHelpers.cs 工具类。
- **状态**: ⏳ 待处理

### 6. [P2] 聚合根属性 init/set 风格不一致
- **维度**: 🧹 代码质量
- **文件**: 多个聚合根（Agent.cs, ApiKey.cs, Workflow.cs 等）
- **描述**: 部分不可变字段用 `private init`，部分用 `private set`。ApiKey 构造函数未校验 expiresAt 为未来时间。
- **建议**: 统一规则：创建后不可变用 `init`，可变用 `set`；expiresAt 加时间范围校验。
- **状态**: ⏳ 待处理

### 7. [P1] JWT 密钥硬编码回退 + 启动时无守卫
- **维度**: 🐛 正确性
- **文件**: `src/AgentPlatform.Api/Program.cs:136,295`
- **描述**: `securitySection["JwtSecretKey"] ?? "dev-secret-key-min-32-chars-long!!"` 在生产环境未配置 JWT 密钥时静默降级为已知密钥。dev-login 端点同样使用此回退。
- **建议**: 启动时验证 JwtSecretKey 是否已从 dev 默认值更改，未配置则启动失败。
- **状态**: ⏳ 待处理

### 8. [P2] ConcurrentDictionary 静态字段无 TTL 驱逐
- **维度**: 🐛 正确性
- **文件**: `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs:35-40`
- **描述**: s_runningCts 和 s_resolvedPresets 是 static ConcurrentDictionary，条目只在 finally 块中按 workflowId 移除。如果进程崩溃或 workflow 被外部删除，条目永远残留。
- **建议**: 添加定时清理（扫描超过阈值时间的条目），或迁移到持久存储。
- **状态**: ⏳ 待处理

### 9. [P2] ConnectionMultiplexer.Connect 非异步 + 无重试
- **维度**: 🐛 正确性
- **文件**: `src/AgentPlatform.Infrastructure/DependencyInjection.cs:135-141`
- **描述**: `ConnectionMultiplexer.Connect(redisConnection)` 使用同步方法，启动时若 Redis 不可用会阻塞线程。无连接重试逻辑。
- **建议**: 使用 `ConnectAsync` + 添加重试/降级策略。
- **状态**: ⏳ 待处理

### 10. [P1] 无基础设施层测试 + 集成测试薄弱
- **维度**: 🧪 测试
- **文件**: 测试项目全局
- **描述**: Infrastructure.Tests 项目不存在。EF Core 映射、仓储 CRUD、AES 加密/解密均无测试。仅 1 个集成测试文件。SpecFlow 步骤类内含编排逻辑。
- **建议**: 创建 Infrastructure.Tests 项目；为仓储/AES/EF 配置写集成测试；为所有控制器加 WebApplicationFactory 测试。
- **状态**: ⏳ 待处理

### 11. [P2] 无 API 契约测试
- **维度**: 🧪 测试
- **文件**: 测试项目全局
- **描述**: 无测试验证 API 响应 JSON 形状、HTTP 状态码或错误格式。开发中接口变更无法被测试捕获。
- **建议**: 添加使用 WebApplicationFactory 的契约测试，验证每个端点的请求/响应模型。
- **状态**: ⏳ 待处理

## 各维度汇总

| 维度 | 问题数 | P0 | P1 | P2 |
|------|--------|----|----|----|
| 🏗️ 架构 | 4 | - | 2 | 2 |
| 🧹 代码质量 | 2 | - | - | 2 |
| 🐛 正确性 | 3 | - | 1 | 2 |
| 🧪 测试 | 2 | - | 1 | 1 |
| **合计** | **11** | **0** | **4** | **7** |

## 本轮亮点

- 架构层最突出的问题是 OrchestrationPrimitive（636 行）的上帝类 + 静态字典泄露风险
- 安全相关的 JWT 硬编码回退是生产环境潜在 P0 风险
- 测试覆盖缺口集中在基础设施层和 API 契约
