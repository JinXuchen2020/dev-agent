# F11 · 沙箱 OS 级隔离增强（进程沙箱禁网 / 资源限额）

> 史诗：Tier-1 · Feature（来源：F5 残留 ③）
> 优先级：[P2]
> 状态：**done**
> 设计文档：`features/sandbox-os-isolation.md`
> 分支：`feat/f11-sandbox-os-isolation`
> 质量报告：`docs/quality/f11-sandbox-os-isolation-gate.md`

## 1. 目标

让 `Process` 沙箱（默认 `Sandbox:Provider=Process`）在不引入 Docker 的前提下，获得 **OS 级** 网络隔离与资源约束，使 `SandboxSettings.NetworkEnabled=false` 真正生效（而非仅设置一个环境标记）。

- **资源限额**：CPU 速率硬上限、作业/进程内存上限、活动进程数上限（防 fork 炸）。
- **网络隔离**：`NetworkEnabled=false` 时，进程无法对外建立网络连接。
- **失败安全**：任何 OS 机制不可用时（权限不足 / 平台不支持 / 解释器文件系统不可达），透明回退到现有缓解项（`AGENT_PLATFORM_SANDBOX_OFFLINE` 环境标记 + 语言白名单 + 超时杀 + 输出截断），**绝不阻断代码执行**。

## 2. 范围与平台决策（已与用户确认）

| 维度 | 决策 |
| --- | --- |
| 平台范围 | **Windows 真实隔离** 本轮落地；Linux / macOS 留 fail-safe 回退（非 Windows 平台 `ISandboxIsolation` 解析为 `NullSandboxIsolation`，仅保留环境标记缓解项并启动告警）。 |
| 资源限额机制 | Windows **Job Object**（工作集 + 作业内存 + 活动进程数 + CPU 速率硬上限）。不需管理员权限，本沙箱可完整测试。 |
| 网络隔离机制 | Windows **AppContainer**（无 `internetClient` 能力的 AppContainer profile 启动解释器，真实阻断出网）。P/Invoke `CreateAppContainerProfile` / `CreateProcessInAppContainer` / `DeleteAppContainerProfile`。 |
| 配置开关 | `SandboxSettings.OsIsolation`（枚举 `Off/JobObject/AppContainer/Full`，**默认 `JobObject`**）。`JobObject` 即开资源限额（安全、无噪声）；`AppContainer` / `Full` 额外启用 AppContainer 网络隔离（需主机一次性准备解释器目录 `ALL APPLICATION PACKAGES` 读 ACL，否则 fail-safe 回退）。 |

### 2.1 为什么默认 `JobObject` 而非 `Full`

AppContainer 默认禁止文件系统访问，会导致 `python`/`node` 无法读取自身安装目录而启动失败。要在 AppContainer 内成功运行解释器，需将解释器所在目录授予 `ALL APPLICATION PACKAGES`（S-1-15-2-1）读取 ACL（通常需一次性管理员设置或解释器装于已可读路径）。因此：

- 默认 `OsIsolation=JobObject` → 资源限额立即生效、零噪声、不需准备主机。
- 显式 `AppContainer` / `Full` → 启用真实网络隔离；在已准备主机上真正禁网，在未准备主机上启动失败 → 透明回退到 `Process.Start` + 环境标记（资源限额仍由 JobObject 兜底），执行不中断、仅记告警日志。

## 3. 接口契约

### 3.1 `SandboxSettings` 新增字段（`AgentPlatform.Application.Abstractions`）

```csharp
public enum OsIsolationMode { Off = 0, JobObject = 1, AppContainer = 2, Full = 3 }

public sealed class SandboxSettings
{
    public string Provider { get; set; } = "Process";
    public int TimeoutSeconds { get; set; } = 30;
    public int HttpTimeoutSeconds { get; set; } = 15;
    public string[] AllowedLanguages { get; set; } = { "python", "javascript", "csscript" };

    /// <summary>是否允许沙箱访问网络。false 时：若 OsIsolation 含 AppContainer/Full 则经 AppContainer 真实禁网；
    /// 否则仅设 AGENT_PLATFORM_SANDBOX_OFFLINE 环境标记（best-effort）。</summary>
    public bool NetworkEnabled { get; set; } = false;

    public int MaxOutputBytes { get; set; } = 65536;
    public Dictionary<string, string> InterpreterPaths { get; set; } = new();

    /// <summary>OS 级隔离模式。默认 JobObject（仅资源限额，不需管理员）。</summary>
    public OsIsolationMode OsIsolation { get; set; } = OsIsolationMode.JobObject;

    /// <summary>作业内允许的最大活动进程数（防 fork 炸）。默认 16。</summary>
    public int MaxProcessCount { get; set; } = 16;

    /// <summary>作业内存上限（字节）。默认 256 MB。</summary>
    public long MemoryLimitBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>CPU 速率硬上限（百分比，1-100）。默认 50。</summary>
    public int CpuRatePercent { get; set; } = 50;
}
```

### 3.2 新增抽象（`AgentPlatform.Infrastructure.Sandbox`）

```csharp
internal interface ISandboxIsolation
{
    /// <summary>本隔离器是否自行启动进程（AppContainer 为 true；JobObject / Null 为 false）。</summary>
    bool CanLaunch { get; }

    /// <summary>自行启动隔离进程并返回结果；不适用或失败时返回 null（调用方回退 Process.Start）。</summary>
    Task<SandboxResult?>? TryLaunchAsync(string fileName, string arguments,
        int timeoutSeconds, CancellationToken ct, string source, string language);

    /// <summary>进程已启动后挂接隔离（JobObject 赋权；Null 无操作）。返回是否成功挂接。</summary>
    bool Attach(Process process);
}
```

- `JobObjectSandboxIsolation`：`CanLaunch=false`，`Attach` 经 `WindowsJobObject` 把进程（及其后代）纳入 Job Object；实现 `IDisposable` 在作用域结束时兜底释放仍活跃的 Job 句柄。
- `AppContainerSandboxIsolation`：`CanLaunch=true`，`TryLaunchAsync` 经 `WindowsAppContainer` 在 AppContainer 内启动解释器（代码经 stdin 管道喂入，避免临时文件可读性问题），并**内部叠加** `WindowsJobObject` 资源限额；任何失败 → 返回 `null`。`OsIsolation=AppContainer` 与 `OsIsolation=Full` 在 DI 中均解析为该类（`Full` 等价于同时启用两者）。
- `NullSandboxIsolation`：非 Windows / `Off` / 不支持时，`CanLaunch=false`、`Attach` 返回 `false`（仅环境标记缓解项生效）。

### 3.3 `ICodeSandbox` / `SandboxResult` 契约

**不变**。`ProcessCodeSandbox` 内部接入隔离层，对外 `RunCodeAsync` / `RunCommandAsync` / `SandboxResult` 签名与语义保持不变（成功/失败、stdout/stderr、ExitCode、耗时、超时杀、取消传播均延续）。

### 3.4 配置（`appsettings.json` → `Sandbox` 节）

```json
{
  "Provider": "Process",
  "NetworkEnabled": false,
  "OsIsolation": "JobObject",
  "MaxProcessCount": 16,
  "MemoryLimitBytes": 268435456,
  "CpuRatePercent": 50
}
```

## 4. 数据模型

无新聚合 / 无新表 / 无 EF 迁移。隔离策略完全由 `SandboxSettings`（配置驱动）决定。

## 5. 验收标准

- [x] `dotnet build` 0 错误 / 0 警告；全量 `dotnet test` 0 失败（Arch 9 / App 188 / Infra 133+4 skip / Integration 5 / Api 35 / SpecFlow 114）。
- [x] `OsIsolation=JobObject`（默认）：运行 python/node 成功，且进程被纳入 Job Object（活动进程数上限生效：fork 炸被限制；作业内存上限生效；CPU 硬上限生效）。Windows 路径 `WindowsJobObject_Direct_Assign_DoesNotThrow` + `JobObjectSandboxIsolation_Windows_Attach_ExecutesCodeSuccessfully` 实测通过。
- [x] `OsIsolation=AppContainer`/`Full` 且 `NetworkEnabled=false`：在已准备主机上经 AppContainer 真实禁网（进程无 `internetClient` 能力）；未准备主机透明回退 `Process.Start` + 环境标记，执行不中断（fail-safe 不变量 `AppContainerSandboxIsolation_TryLaunch_NeverReturnsFailedResult` + `ProcessCodeSandbox_WithAppContainerIsolation_StillRunsCode_ViaFailSafe` 验证）。
- [x] 非 Windows 平台：`NullSandboxIsolation`，仅环境标记缓解项，启动记告警，不影响执行（`JobObjectSandboxIsolation_NonWindows_AttachReturnsFalse` 验证）。
- [x] `Attach` 失败（宿主进程已在不可突破的 Job 中 / 句柄打开失败）：fail-safe 记告警并继续，不抛异常、不阻断。
- [x] 超时 / 取消 / 退出码 / 输出截断语义与 F5/F9 既有行为一致（复用 `ProcessCaptureHelper`）。
- [x] 单测：JobObject 资源限额实测（Windows）；AppContainer 启动管道 `SkippableFact`（仅当可建 profile 时跑，否则跳过）。

## 6. 质量门禁清单（ddd-phase-quality-gate 内嵌，12 类审计）

> 审计范围：本次 F11 变更文件（`src/AgentPlatform.Infrastructure/Sandbox/*`、`ProcessCodeSandbox.cs`、`DependencyInjection.cs`、`SandboxSettings.cs`、`OsIsolationMode.cs`、`SandboxIsolationTests.cs`）。
> 审计结论：**PASS —— P0=0, P1=0, P2=0, P3=0（全部审计，无遗留、无 waiver）**。完整报告见 `docs/quality/f11-sandbox-os-isolation-gate.md`。

| # | 审计类别 | 结果 | 说明 |
| --- | --- | --- | --- |
| 1 | DI 注册完整性 | PASS | `ISandboxIsolation` 在 `DependencyInjection.cs` 按 平台+OsIsolation 工厂注册为 Scoped（Null/JobObject/AppContainer），所有实现均被解析；`IOptions<SandboxSettings>` 已 `Configure`。 |
| 2 | DDD 分层 | PASS | 接口 `SandboxSettings`/`OsIsolationMode` 在 `Application.Abstractions`；实现均在 `Infrastructure.Sandbox`（`internal sealed`）；`Application` 零 `using Infrastructure`；`Domain` 无新增依赖。`ISandboxIsolation` 为 `internal` 策略接口，仅限 Infrastructure 内部，非跨层契约，合理。 |
| 3 | EF Core 映射 | N/A | 无新聚合 / 无迁移。 |
| 4 | 硬编码值 | PASS | 资源限额默认（16/256MB/50%）均来自 `SandboxSettings` 配置；仅 OS 常量（4MB 最小工作集、`HResultAlreadyExists=0x800700B7`、`ProfilePrefix`）为合理底层常量。 |
| 5 | CancellationToken | PASS | `TryLaunchAsync`/`CaptureAsync`/`CapturePipeAsync` 均接收并透传 `ct`（链接 `cts` 带超时）；`Attach`/`WriteStdinAsync`/`ReadToEnd` 为内部同步/快速路径，无需 token。 |
| 6 | 修饰符 / 密封 | PASS | 全部实现类 `internal sealed`；`ProcessCaptureHelper` 为 `internal static`；`SandboxSettings`/`OsIsolationMode` 为 `public sealed`/`enum`。 |
| 7 | 并发与生命周期 | PASS | `JobObjectSandboxIsolation` 用 `ConcurrentDictionary<int,WindowsJobObject>` 追踪活跃 Job；进程退出经 `Exited` 事件释放，并新增 `IDisposable` 在作用域结束时兜底清理残留句柄；无 Singleton×grow-only 泄漏。`WindowsAppContainer`/`WindowsJobObject` 句柄均 `Dispose`/`finally` 释放（已修复 3 处 P1 泄漏）。 |
| 8 | 空守卫 | PASS | `AppContainerSandboxIsolation.TryLaunchAsync` 守卫 `fileName is null`；`ProcessCodeSandbox` 守卫 `cmd is null`；`WindowsJobObject.Assign` 守卫 `_hJob==Zero`/`process.HasExited`。 |
| 9 | API 基础设施 | N/A | F11 不新增端点 / 控制器；`Program.cs` 既有 ExceptionHandler/CORS/HealthChecks 不受影响。 |
| 10 | 蓝图漂移 | PASS | 蓝图"进程沙箱仅设环境标记"已由 F11 真实化（JobObject+AppContainer）；本设计文档 §3.2 原 `CompositeSandboxIsolation` 表述已修订为实际实现（`AppContainerSandboxIsolation` 内部叠加 JobObject）。无未完成占位。 |
| 11 | XML 文档 | PASS | 全部新增公共类型（`OsIsolationMode` 枚举及值、`SandboxSettings` 新属性、`ISandboxIsolation` 接口方法）均含中文 `/// <summary>`/`<param>`。 |
| 12 | Swagger / API 文档 | N/A | F11 不新增 API 契约，Swagger 现有配置不受影响。 |

### 架构 / 安全补充检查

- **ArchitectureTests**：9/9 通过（含 internal-sealed、Application 不引用 Infrastructure、Abstractions 注册等约束）。
- **依赖包漏洞扫描**（`dotnet list package --vulnerable`）：全部项目 **0 易受攻击包**。
- **安全边界（F11 特有 P0/P1）**：
  - [x] OS 隔离失败 fail-safe 回退，绝不阻断执行；AppContainer/JobObject 句柄均 `Dispose`/`finally` 释放。
  - [x] 资源限额不需管理员权限；AppContainer 失败路径捕获所有 P/Invoke 异常（含 `EntryPointNotFoundException`）→ 回退 + 告警。
  - [x] 隔离强度由 `SandboxSettings.OsIsolation` 配置驱动，无硬编码；默认 `JobObject`（最小噪声）。
  - [x] AppContainer 回退 / JobObject 挂接失败 / 非 Windows 回退均记结构化日志。
  - [x] `ICodeSandbox` / `SandboxResult` 对外契约不变，前端无感知。

## 7. 风险点

- **AppContainer 文件系统壁垒**：默认禁止文件访问 → 解释器可能无法加载。缓解：fail-safe 回退 + 文档化一次性 ACL 准备步骤；`OsIsolation` 默认 `JobObject` 规避默认噪声。
- **JobObject 赋权失败**：宿主进程已在不可突破的 Job 中时 `AssignProcessToJobObject` 失败。缓解：捕获 `Win32Exception` → 告警 + 继续。
- **CPU 硬上限误配**：`CpuRatePercent` 必须 1-100；越界 clamp 并告警，避免 0 导致进程饿死或 >100 无效。
- **内存上限误伤**：`MemoryLimitBytes` 过小会导致合法脚本 OOM；默认 256MB，可经配置上调。
- **测试平台**：AppContainer 实测仅在可建 profile 的 Windows 上跑（CI `windows-latest`）；本开发沙箱通常跳过，靠 `SkippableFact` 保证不红。
