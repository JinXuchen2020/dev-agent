# Codebase Optimizer - 变更日志

## Round 1

### 2026-07-22
- [R1-T5] 重构 | `src/AgentPlatform.Infrastructure/Shared/StringHelpers.cs` | 创建共享工具类
- [R1-T5] 重构 | `src/AgentPlatform.Infrastructure/Workflows/AgentCallStepExecutor.cs:106-107` | 私有 Truncate 改为调用 StringHelpers
- [R1-T5] 重构 | `src/AgentPlatform.Infrastructure/Workflows/CriticStepExecutor.cs:180-181` | 私有 Truncate 改为调用 StringHelpers
- [R1-T5] 重构 | `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs:567-568` | 私有 Truncate 改为调用 StringHelpers
- [R1-T8] 修复 | `src/AgentPlatform.Infrastructure/DependencyInjection.cs:135-141` | ConnectionMultiplexer 添加重试+超时配置+日志
