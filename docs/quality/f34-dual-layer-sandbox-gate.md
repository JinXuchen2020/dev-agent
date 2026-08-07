# F34 质量门报告 · 沙箱双层隔离（Docker 默认强隔离 + JobObject/AppContainer 兜底）

- 分支：`feat/f34-dual-layer-sandbox`（基于已合并 master = 含 F9/F11）
- 模式：codebase-optimizer **分析模式**（不建分支 / 不 push / 不破坏性修改，遵守 feature-builder 硬约束）
- 构建：`dotnet build src/AgentPlatform.sln` → **0 警告 0 错误**
- 测试：全量 `dotnet test` → **0 失败**（Arch9 / App188 / Infra139+5skip / Integration5 / Api35 / SpecFlow114）

---

## Gate 1 · ddd-code-reviewer（对抗式逐行评审）

**状态：PASS（实施期发现并即时修复 3 项，无遗留阻断）**

| 严重度 | 类别 | 文件 | 发现 | 修复 |
|--------|------|------|------|------|
| **P1** | 行为错误（能力在、行为错） | `DockerSandboxIsolation.cs` `TryLaunchAsync` | 委托 `DockerCodeSandbox.RunCodeAsync` 返回的 `SandboxResult` 用 5 参构造默认 `Weak`，F34 核心契约「Docker 路径标 Strong」被静默违反；CI `ubuntu-latest` 测试会失败 | `return result with { IsolationStrength = IsolationStrength.Strong };` |
| **P2** | 文档/实现漂移 | `dual-layer-sandbox-isolation.md` §2/§3/§5/§6 | 声称 `ReadonlyRootfs`/`seccomp` 生效，实际 `ReadonlyRootfs=false`（仅代码 `:ro` 挂载） | 校正 4 处表述为「内存限额 + 只读代码挂载（:ro）」，类注释同步；运行时行为不变 |
| **P3** | 测试缺陷 | `DualLayerSandboxTests.cs` `DockerSandboxIsolation_Attach_ReturnsFalse` | 裸 `[Fact]` 调 `Process.Start("echo")` 在 Windows CI 失败 | 改 `[SkippableFact]` + `Skip.If(Windows)`（同 F11 同构） |

资源生命周期（H2）核查：`DockerProbe` 单例无集合；`JobObjectSandboxIsolation` scoped + 进程退出即移除 + `IDisposable` 兜底；`DockerCodeSandbox` 容器 `finally` 强制 `RemoveContainerAsync`。句柄获取/释放对称性覆盖全出口 VERIFIED。

---

## Gate 2 · ddd-phase-quality-gate（12 类审计）

**状态：PASS [P0:0 | P1:0 | P2:0 | P3:0]**

| # | 类别 | 结果 | 证据 |
|---|------|------|------|
| 1 | DI 注册缺口 | 通过 | `ICodeSandbox→ProcessCodeSandbox`、`IDockerProbe`(singleton)、`DockerCodeSandbox`(scoped 内部执行器)、`ISandboxIsolation`(工厂) 全部注册（DependencyInjection.cs:180-204） |
| 2 | DDD 层违规 | 通过 | Application 零 `using AgentPlatform.Infrastructure`；接口在 Abstractions、实现在 Infrastructure |
| 3 | EF Core 映射缺口 | 不适用 | F34 无新增聚合/实体 |
| 4 | 硬编码值 | 通过 | F34 未引入新魔法数；探测 2s 超时为局部常量、非安全敏感 |
| 5 | 缺失 CancellationToken | 通过 | `TryLaunchAsync`/`RunCodeAsync` 均携 `ct` 并下传 `_docker.RunCodeAsync` |
| 6 | 缺失修饰符 | 通过 | 6 个隔离类全部 `internal sealed` |
| 7 | 并发风险 | 通过 | `DockerProbe` 单例无集合；`JobObjectSandboxIsolation._active` scoped + 退出移除 + Dispose 兜底，无 grow-only Singleton |
| 8 | 缺失 null 守卫 | 通过 | `ProcessCodeSandbox` 对 `cmd`/`fileName` 做 null 校验 |
| 9 | API 基础设施 | 不适用 | F34 不改 HTTP 端点/中间件 |
| 10 | 蓝图漂移 | 已修复 | P2 `ReadonlyRootfs` 表述漂移已校正（见 Gate 1） |
| 11 | 缺失 XML 文档 | 通过 | `IsolationStrength`/`SandboxResult`/`ICodeSandbox` 均含中文 `///` |
| 12 | Swagger/API 文档 | 不适用 | F34 无新增公开端点 |
| — | 死代码/空壳类 | 通过 | `IsolationStrength` 三成员（None/Weak/Strong）均被引用；无零引用清理 API；`DockerSandboxIsolation`/`DockerProbe` 均有真实调用点 |

审计明细与逐项结论见 `features/dual-layer-sandbox-isolation.md` §6.1。

---

## Gate 3 · codebase-optimizer（七维分析，分析模式）

**状态：PASS（七维 0 阻断；仅 2 项 P3 观察项，均不阻塞，留观）**

| 维度 | 结论 |
|------|------|
| 架构 | DDD 分层正确：新增 `IsolationStrength`/`SandboxResult.IsolationStrength` 在 `Application.Abstractions`；`DockerProbe`/`DockerSandboxIsolation` 在 `Infrastructure.Sandbox` 且 `internal sealed`；`ISandboxIsolation` 为 internal 策略接口；DI 工厂按 `Provider + 探测 + 平台` 条件解析。无跨层引用。 |
| 代码质量 | 统一中文 XML 文档；`DockerSandboxIsolation` 显式注释「忽略 fileName/arguments、Docker 走代码注入」消除误导。 |
| 正确性 | 全量 `dotnet test` 0 失败；P1 强度标注 bug 已修复（见 Gate 1）。 |
| 测试 | F34 新增 7 项单测（探测 fail-safe / 模式切换 / `Attach=false` / Strong 结果 SkippableFact / 回退 Weak Skippable / 向后兼容）；F11 兜底路径全绿。Docker 真实执行路径以 SkippableFact 覆盖（CI `ubuntu-latest` 有 daemon 时断言 Strong）。 |
| 性能 | `DockerProbe` 单例构造一次同步 `PingAsync().Wait(2000)`，加超时 + 全 `try/catch` fail-safe，无热路径分配；`DockerSandboxIsolation.TryLaunchAsync` 仅一次 `SandboxResult with` 拷贝（微量）。无热路径阻塞/泄漏。 |
| 安全 | 复用 F9 `DockerCodeSandbox`：`NetworkMode=none`（默认 `NetworkEnabled=false` 离线）+ `Memory=256MB` 限额 + 代码 `:ro` 挂载；无密钥/命令落库；失败路径全捕获回退不阻断。 |
| 工程化 | `dotnet build` 0 警告 0 错误；无新增 NuGet 依赖（复用 F9 `Docker.DotNet`）；`dotnet list package --vulnerable` 无新增 CVE。 |

### P3 观察项（留观，不阻塞、本次不修）

1. **`DockerProbe.Probe()` 同步-over-异步**：`PingAsync().Wait(TimeSpan.FromSeconds(2))` 在单例构造中。为避免 async ctor，采用一次性同步探测可接受；若后续要求全异步，可改为 `IHostedService` 延迟探测，但会引入启动竞态，当前方案更稳。
2. **`ProcessCodeSandbox.Truncate` 按字符数而非字节数截断**：`MaxOutputBytes` 命名为「字节」但实际用 `value.Length`（UTF-16 字符数）截断。对多字节输出（如中文）截断粒度非精确字节。系 F11 既有行为，非 F34 引入；建议后续单独立项改为按 `Encoding.UTF8.GetByteCount` 估算或流截断。

---

## 综合结论

三道质量门对 F34 增量**均 0 阻断**：

- Gate 1（ddd-code-reviewer）：3 项发现（1 P1 + 1 P2 + 1 P3）已全部修复。
- Gate 2（ddd-phase-quality-gate）：12 类审计全过，无新增阻断。
- Gate 3（codebase-optimizer）：七维 0 阻断，2 项 P3 留观。

`.quality-gate.json` 已更新（`cleared:true` + `codebaseOptimizer` 字段），与本次变更同笔暂存以满足 pre-commit 钩子。按 feature-builder 硬约束 **不 push、不 merge**。

---

## 提交后 CI 修复（F34 合入主干前流水线暴露）

**状态：已修复（新增 1 次提交，不 push）**

| 严重度 | 类别 | 文件 | 现象 | 根因 | 修复 |
|--------|------|------|------|------|------|
| **P2** | 测试/环境耦合 | `DualLayerSandboxTests.cs` `DockerProbe_NoDaemon_IsUnavailable` | `ubuntu-latest` 流水线该测试 FAIL：`Assert.False()` 期望 False，实际 True | 该测试写死 `[Fact]` 假设「无 Docker 守护进程」并断言 `IsAvailable==false`；但 GitHub `ubuntu-latest`  runner **自带 Docker 守护进程**，`DockerProbe` 探测成功返回 `true` → 断言失败。此为测试假设错误，非实现缺陷（`DockerProbe` 探测逻辑正确） | 改为 `[SkippableFact]` + `Skip.If(probe.IsAvailable, "Docker 守护进程可用，无法验证无 daemon 的 fail-safe 路径")`：有 daemon 环境（CI）跳过，无 daemon 环境（本沙箱 / 裸机）执行断言 |

**对称性说明**：互补测试 `DockerSandboxIsolation_DockerAvailable_ReturnsStrongResult` 已用 `Skip.IfNot(probe.IsAvailable, …)`。两测试组合后跨环境覆盖完整：
- CI（有 Docker）：跑 Strong-result 实测，跳过 NoDaemon 断言。
- 本沙箱 / 裸机（无 Docker）：跑 NoDaemon 断言，跳过 Strong-result。

**验证**：本沙箱重跑 `dotnet test --filter Sandbox` → 21 通过 / 6 跳过 / 0 失败；全量 `dotnet test` → 0 失败（Arch9 / App188 / Infra138+6skip / Integration5 / Api35 / SpecFlow114）。
