# F34 · 沙箱双层隔离（Docker 默认强隔离 + JobObject/AppContainer 兜底）

> 状态：**doing**
> 优先级：**[P0 最高优先级]** ｜ 风险：**⚠️中风险**（跨 F9/F11 集成；Docker 可用性探测 + 模式选择）
> 所属：Tier 1（feature-builder 消费单元）｜ 置顶：feature-builder 下一个
> 分支：`feat/f34-dual-layer-sandbox`（基于已合并 master，含 F9/F11）

## 1. 立项动机

来自 `docs/sandbox-isolation-harness-comparison.md` §7「收敛差距建议」：

- F11 的 `JobObject` + `AppContainer` 是**同内核 OS 原语**隔离，失败策略为 **fail-safe 开放**——任何 OS 机制建不起来就降级成环境标记缓解项，**绝不阻断代码执行**。真实禁网依赖宿主对解释器目录预先授予 `ALL APPLICATION PACKAGES` 读 ACL，否则 AppContainer 路径透明回退、无真禁网。
- 主流 harness（E2B / OpenAI）用 **VM 边界**（Firecracker/KVM）做确定性隔离，失败策略为 **fail-closed**。
- 差距：F11 在「无 Docker 的 Windows dev 机」上获得轻量隔离，但宿主安全是同内核弱隔离；要让隔离**确定性生效**（VM 级网络策略 + read-only rootfs + seccomp），应引入 Docker/gVisor。

**结论（本 feature 采纳）**：**两层并存**——默认走 Docker 强隔离，无 daemon 时降级到 F11 的 JobObject/AppContainer 兜底，并显式告知用户「隔离 weaker」。这正与 F9（Docker 真实化）和 F11（进程级兜底）的既有分工一致。

## 2. 目标

- 在现有 `ISandboxIsolation` 抽象（F11 引入）上新增 `DockerSandboxIsolation`：`Provider=Docker` 且守护进程可用时**默认启用**，获确定性强隔离（容器 `NetworkMode=none` + 内存限额 + 只读代码挂载 `:ro`，全部复用 F9 `DockerCodeSandbox`）。
- `ProcessCodeSandbox` 作为**唯一 `ICodeSandbox` 入口**不变，隔离策略全交给 `ISandboxIsolation`；`DockerCodeSandbox` 不再作为并列 `ICodeSandbox` 注册，改由 `DockerSandboxIsolation` 持有并复用其容器执行逻辑。
- 对外契约**基本不变**：`SandboxResult` 仅**非破坏地**新增 `IsolationStrength` 字段（枚举，末尾带默认值），既有 5 参构造调用全部继续编译。
- Docker 不可用 / `Provider=Process` / `OsIsolation=Off` 时，明确回退 F11 `JobObjectSandboxIsolation` / `AppContainerSandboxIsolation`，并以 `SandboxResult.IsolationStrength` + 结构化日志声明隔离强度（strong / weak / none）。

## 3. 复用点（不重复造轮子）

- **Docker 执行路径**：直接复用 F9 已真实化的 `DockerCodeSandbox`（`Docker.DotNet` 3.125.15；`NetworkMode=none` + `Memory=256MB` + 代码只读挂载 `:ro` + `Tty=true`；`SafeReadLogsAsync` 的 tty 参数已在 CI 回归中修正为 `true`）。本 feature 不重写容器执行，只新增「模式选择 + 探测 + 兜底链路 + 显式强度标注」。
- **进程级兜底**：复用 F11 的 `JobObjectSandboxIsolation`（Windows Job Object 资源限额）+ `AppContainerSandboxIsolation`（AppContainer 真实禁网）+ `NullSandboxIsolation`（非 Windows/Off）。

## 4. 方案（细化）

### 4.1 架构

```
ICodeSandbox (统一入口)
  └─ ProcessCodeSandbox
       └─ ISandboxIsolation  (按 Provider / Docker 可用 / OS 解析)
            ├─ DockerSandboxIsolation      (CanLaunch = Docker 守护进程可用)
            │     └─ 复用 DockerCodeSandbox.RunCodeAsync  → IsolationStrength=Strong
            ├─ AppContainerSandboxIsolation (Windows + AppContainer/Full) → Weak
            ├─ JobObjectSandboxIsolation    (Windows + JobObject/默认)     → Weak
            └─ NullSandboxIsolation         (非 Windows / Off)             → None
```

`ProcessCodeSandbox.RunCodeAsync` 已有 `if (_isolation.CanLaunch) → TryLaunchAsync(...)` 分支；Docker 模式下 `DockerSandboxIsolation.TryLaunchAsync` 委托 `DockerCodeSandbox.RunCodeAsync(source, language, ...)` 并返回 `Strong` 结果；任何失败返回 `null` → 透明回退到常规 `Process.Start` + `Attach`（F11 资源限额兜底）。

### 4.2 新增类型

- `IsolationStrength` 枚举（`Application/Abstractions`）：`None` / `Weak` / `Strong`。
- `SandboxResult` record 末尾追加 `IsolationStrength IsolationStrength = IsolationStrength.Weak`（带默认值的 positional 参数 → 向后兼容）。
- `IDockerProbe` 单例（`Infrastructure/Sandbox`）：构造时一次 `DockerClientConfiguration().CreateClient().PingAsync()`（短超时 `try/catch`）缓存 `IsAvailable`；不可用记告警。供给 `ISandboxIsolation` 工厂决策，避免每次执行都探测。
- `DockerSandboxIsolation : ISandboxIsolation`（`Infrastructure/Sandbox`）：注入 `IDockerProbe` + `DockerCodeSandbox` + `ILogger`；`CanLaunch => _probe.IsAvailable`；`Strength => Strong`；`TryLaunchAsync` 委托 `DockerCodeSandbox.RunCodeAsync(source, language, timeoutSeconds, ct)`；`Attach` 为 noop（容器自带隔离）；`TryLaunchAsync` 异常 → 记告警 + 返回 `null`（fail-safe）。
- `ISandboxIsolation` 接口扩展：新增 `IsolationStrength Strength { get; }` 属性（F11 三实现分别返回 `Weak`/`Weak`/`None`）。`ProcessCodeSandbox` 在构造 `SandboxResult` 时填入 `_isolation.Strength`，使无论走哪条路径结果都带正确强度。

### 4.3 DI 工厂（`DependencyInjection.cs`）

- `ICodeSandbox` → 始终注册 `ProcessCodeSandbox`（移除原 `Provider=Docker → DockerCodeSandbox` 的并列注册）。
- 先注册 `IDockerProbe`（singleton）与 `DockerCodeSandbox`（作为内部执行器，不暴露 `ICodeSandbox`）。
- `ISandboxIsolation` 解析（scoped/transient）：
  - `Provider=="Docker"` 且 `probe.IsAvailable` → `DockerSandboxIsolation`
  - 否则按 F11 规则：Windows + (AppContainer|Full) → `AppContainerSandboxIsolation`；Windows + (JobObject|默认) → `JobObjectSandboxIsolation`；非 Windows/Off → `NullSandboxIsolation`。

### 4.4 配置（`appsettings.json` Sandbox 节）

- `Provider` 改默认 `"Docker"`（默认强隔离；守护进程不可用自动降级，fail-safe）。
- 保留 `OsIsolation`（JobObject 默认）、`NetworkEnabled`、`MaxProcessCount`/`MemoryLimitBytes`/`CpuRatePercent` 等；加注释说明「Docker 优先，失败回退进程级隔离」。

### 4.5 范围边界（明确不做）

- **命令路径**（`ProcessCodeSandbox.RunCommandAsync`，shell 命令）：**保持 F11 行为**（Process.Start + JobObject 资源限额），**不经 Docker**。理由：沙箱主路径是用户代码（python/node）执行；shell 命令为辅助（集成测试用），F11 资源限额已覆盖，本 feature 聚焦代码执行的 Docker 强隔离以控范围与风险。若后续需命令也 Docker 化，单独立项。
- 不引入 gVisor / Firecracker / 新 NuGet 包；Docker 路径仍用 F9 已引的 `Docker.DotNet`。
- 不改 `ICodeSandbox` 方法签名（无前端契约变更，前端无沙箱配置 UI，同 F10/F11）。

## 5. 验收标准

- [ ] `dotnet build` 0 错误 / 0 警告；全量 `dotnet test` 0 失败。
- [ ] `Provider=Docker` 且守护进程可用 → `DockerSandboxIsolation.CanLaunch==true`，代码经容器执行，`SandboxResult.IsolationStrength==Strong`，容器 `NetworkMode=none` + `Memory` 限额 + 代码只读挂载（`:ro`）生效（CI `ubuntu-latest` 实测）。
- [ ] Docker 不可用 / `Provider=Process` → 回退 F11 JobObject（默认）+ 可选 AppContainer；`SandboxResult.IsolationStrength==Weak`（Windows）/ `None`（非 Windows）；行为与 F11 既有测试一致。
- [ ] `IDockerProbe` 一次探测；不可用不抛异常，记告警并静默降级（fail-safe）。
- [ ] 单测：Docker 路径 `SkippableFact`（本沙箱无 daemon 跳过；CI 实测）；新增 `DockerSandboxIsolation` 探测失败 `CanLaunch=false` 断言 + `ProcessCodeSandbox` 强度回退断言；F11 兜底路径全绿。
- [ ] 模型一致性：仅 `SandboxResult` 新增非破坏枚举字段，无前端消费；`tsc` 无需改动（无前端变更）。
- [ ] 文档：同步 CHANGELOG / BLUEPRINT / appendices；对比文档 §7「收敛差距建议」标记已立项落地。

## 6. 质量门禁清单（ddd-phase-quality-gate 内嵌）

- [ ] **P0 安全边界**：Docker 不可用必须 fail-safe 回退 F11 路径，绝不阻断代码执行；`DockerCodeSandbox` 容器资源/网络参数（NetworkMode=none、Memory 限额、只读代码挂载 `:ro`）不得被本 feature 弱化。
- [ ] **P0 探测健壮性**：`IDockerProbe` 探测必须短超时 + 全 `try/catch`，异常 → `IsAvailable=false`，不抛、不阻塞启动。
- [ ] **P1 分层**：`DockerCodeSandbox` 不再作为并列 `ICodeSandbox` 暴露；`DockerSandboxIsolation` 仅经 `ISandboxIsolation` 接入；`Application` 不引用 `Infrastructure`。
- [ ] **P1 契约兼容**：`SandboxResult` 新增字段带默认值，既有 5 参构造调用全部编译通过；`ICodeSandbox` 方法签名不变。
- [ ] **P1 失败安全**：`DockerSandboxIsolation.TryLaunchAsync` 任意异常 → 记告警 + 返回 `null`，调用方回退 `Process.Start` + `Attach`。
- [ ] **P1 配置驱动**：隔离模式由 `Sandbox.Provider` + 探测结果决定，无硬编码；`OsIsolation` 语义不变。
- [ ] **P2 可观测**：Docker 回退、探测失败、隔离强度（Strong/Weak/None）均记结构化日志；`SandboxResult.IsolationStrength` 供调用方观测。
- [ ] **P2 测试覆盖**：Docker 路径 `SkippableFact` + 探测失败 `CanLaunch=false` + 强度回退；全量 `dotnet test` 0 失败。
- [ ] **P3 文档**：CHANGELOG / BLUEPRINT / appendices 沙箱说明同步到双层隔离实现，消除漂移。
- [ ] **P3 无契约破坏**：前端无沙箱 UI，无前端变更；`SandboxResult` 字段向后兼容。

### 6.1 十二类质量审计追踪（ddd-phase-quality-gate）

实施 + 代码评审后跑全量 12 类审计，结论如下（均在该 feature 范围内，无遗留阻断项）：

| # | 类别 | 结果 | 证据 |
|---|------|------|------|
| 1 | DI 注册缺口 | 通过 | `ICodeSandbox→ProcessCodeSandbox`、`IDockerProbe`(singleton)、`DockerCodeSandbox`(scoped 内部执行器)、`ISandboxIsolation`(工厂) 全部注册（DependencyInjection.cs:180-204） |
| 2 | DDD 层违规 | 通过 | Application 工程零 `using AgentPlatform.Infrastructure`；接口在 Abstractions、实现在 Infrastructure |
| 3 | EF Core 映射缺口 | 不适用 | F34 无新增聚合/实体 |
| 4 | 硬编码值 | 通过 | F34 未引入新魔法数；探测 2s 超时为局部常量，非安全敏感 |
| 5 | 缺失 CancellationToken | 通过 | `TryLaunchAsync`/`RunCodeAsync` 均携 `ct` 并下传至 `_docker.RunCodeAsync` |
| 6 | 缺失修饰符 | 通过 | 6 个隔离类全部 `internal sealed`（含 `IDisposable` 的 JobObject） |
| 7 | 并发风险 | 通过 | `DockerProbe` 单例无集合；`JobObjectSandboxIsolation._active` 为 scoped 且进程退出即移除（OnExited + Dispose 兜底），无 grow-only Singleton |
| 8 | 缺失 null 守卫 | 通过 | `ProcessCodeSandbox.RunCodeAsync`/`RunProcessAsync` 对 `cmd`/`fileName` 做 null 校验 |
| 9 | API 基础设施 | 不适用 | F34 不改 HTTP 端点/中间件 |
| 10 | 蓝图漂移 | 已修复 | P2 `ReadonlyRootfs` 表述漂移已校正（§7.1） |
| 11 | 缺失 XML 文档 | 通过 | `IsolationStrength`/`SandboxResult`/`ICodeSandbox` 均含中文 `///` |
| 12 | Swagger/API 文档 | 不适用 | F34 无新增公开端点 |
| — | 死代码/空壳类 | 通过 | `IsolationStrength` 三成员（None/Weak/Strong）均被引用；无零引用清理 API；`DockerSandboxIsolation`/`DockerProbe` 均有真实调用点 |

**Gate Status: PASS** [P0:0 | P1:0 | P2:0 | P3:0]（代码评审修复的 P1/P2/P3 已计入 §7.1，本审计无新增阻断项）。

## 7. 风险

- **中**：跨 F9/F11 集成，Docker 探测时机与失败回退链需严谨，避免探测阻塞启动或回退路径与 F11 行为漂移。
- 缓解：探测设超时 + `try/catch` 全包且单例化（仅一次）；回退路径直接复用 F11 已验证实现；Docker 路径 `SkippableFact` 不污染本沙箱测试。

## 7.1 代码评审修订记录（ddd-code-reviewer）

实施完成后经对抗式评审（逐行读码 + 控制流追踪 + 资源生命周期 + 死代码/接线核查），发现并即时修复如下：

| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| **P1** | 行为错误（能力在但行为错） | `DockerSandboxIsolation.cs` `TryLaunchAsync` | 委托 `DockerCodeSandbox.RunCodeAsync` 返回的结果用 5 参 `SandboxResult` 构造，默认 `IsolationStrength=Weak`。F34 核心契约「Docker 路径标注 Strong」被静默违反；CI `ubuntu-latest` 实测测试 `DockerSandboxIsolation_DockerAvailable_ReturnsStrongResult` 会失败。 | `return result with { IsolationStrength = IsolationStrength.Strong };`（record `with` 表达式，零侵入升级强度；`DockerCodeSandbox` 不改动）。 |
| **P2** | 文档/实现漂移 | `dual-layer-sandbox-isolation.md` §2/§3/§5/§6 | 多次声称 `ReadonlyRootfs` 生效，但 `DockerCodeSandbox.BuildContainerConfig` 实际 `ReadonlyRootfs=false`（仅代码 bind 挂载 `:ro`）。真实强隔离参数为 NetworkMode=none + Memory 限额 + 只读代码挂载。 | 修正设计文档四处 `ReadonlyRootfs`/`seccomp` 表述为「内存限额 + 只读代码挂载（:ro）」；`DockerSandboxIsolation` 类注释同步。运行时行为不变（rootfs 置只读会威胁解释器临时写入，故不实启）。 |
| **P3** | 测试缺陷 | `DualLayerSandboxTests.cs` `DockerSandboxIsolation_Attach_ReturnsFalse` | 裸 `[Fact]` 调 `Process.Start("echo", ...)`；`echo` 仅非 Windows 可执行，Windows CI 下 `Process.Start` 返回 null/抛异常导致 `Assert.NotNull` 失败。 | 改为 `[SkippableFact]` + `Skip.If(Windows)`（与 F11 `JobObjectSandboxIsolation_NonWindows_AttachReturnsFalse` 同构）；`Attach` 在所有平台恒返回 false。 |

验证：`dotnet build` 0/0；`dotnet test --filter Sandbox` 21 通过 / 6 跳过（Docker 路径 SkippableFact 本沙箱无 daemon 跳过；CI `ubuntu-latest` 有 daemon 时 `DockerAvailable_ReturnsStrongResult` 将覆盖 P1 修复）。

## 8. 参考

- `docs/sandbox-isolation-harness-comparison.md`（§7 收敛差距建议 —— 本 feature 的动机来源）
- `features/sandbox-os-isolation.md`（F11 设计文档）
- `features/sandbox-docker.md`（F9 设计文档）
- `docs/quality/f11-sandbox-os-isolation-gate.md`、`docs/quality/f9-docker-sandbox-gate.md`
- `src/AgentPlatform.Infrastructure/Sandbox/`（F11 隔离层 + F9 DockerCodeSandbox）
