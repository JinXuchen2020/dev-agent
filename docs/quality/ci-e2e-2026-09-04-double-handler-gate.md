# CI E2E 复跑双败修复质量门（2026-09-04）

## 范围（scoped fix）
聚焦 2026-09-04 CI 前端 E2E 复跑 2 败（27 passed）：

1. **F40 execution-log-replay：exactly-1 断言得 2**
   - 根因（平台级，非测试隔离问题）：`WorkflowStartedEventHandler` 被注册了**两次**——
     `AddApplication` 的 `cfg.RegisterServicesFromAssembly(Application 程序集)`（MediatR 12.4.1 对
     `INotificationHandler<>` 走 `addIfAlreadyExists=true` 分支，即普通 `AddTransient`，**非 TryAdd**，
     源码 ServiceRegistrar.cs 已核实）+ `Infrastructure/DependencyInjection.cs` 对同一批
     Application.EventHandlers 类型的显式 `AddScoped` 注册。MediatR 发布通知时解析
     `IEnumerable<INotificationHandler<T>>`，两个注册各执行一次 → **每次 run 产生 2 条 ExecutionLog**。
     与此前步骤注释里「POST /workflows 留下两条日志」的历史观察完全吻合（当时被误归因为创建即跑）。
   - 修复：删除 Infrastructure 的 7 处显式 `INotificationHandler` 注册（5 个事件处理器 +
     SemanticMemoryWriteBackHandler×2 接口）。Infrastructure 程序集内无任何 INotificationHandler
     实现（grep 证实），扫描是唯一注册源，无遗漏。transient vs scoped：处理器仅依赖 scoped 服务，
     请求内解析等价。
   - 波及面评估：StepCompleted/StepFailed 等处理器此前同样双跑（日志条目成对重复），去重后单跑；
     全量后端测试回归验证。

2. **F35 workspace-switch：combobox click 被 selection-item 拦截（60s 卡满）**
   - 根因：antd Select 已有选中值时 `.ant-select-selection-item` span 盖住 combobox input，
     Playwright 命中目标检查失败无限重试。该步骤此前从未在 CI 跑到（前次卡在「确认」按钮，
     93763e6 修复后才第一次走到这里）。
   - 修复：`workspace.steps.ts` 两处 combobox click 改 `click({ force: true })`——与
     `credentials.steps.ts:28` 已验证先例完全同款（该处注释即记录了同一拦截坑）。

## 改动
- `src/AgentPlatform.Infrastructure/DependencyInjection.cs`：删除重复注册（净 -7 行 + 注释）。
- `src/AgentPlatform.Web/e2e/steps/workspace.steps.ts`：2 处 `click({ force: true })` + 注释。

## 校验
- build 0/0；后端全量回归（结果见提交信息）；bddgen exit 0、tsc 0 error。
- Optimizer：PASSED (Round 1, 0 open, scoped to this fix)。

## 结论
cleared=true。真实浏览器 E2E 交 CI 验证。
