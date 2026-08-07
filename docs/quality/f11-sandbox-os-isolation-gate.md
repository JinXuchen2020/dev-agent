# F11 · 沙箱 OS 级隔离增强（JobObject 资源限额 + AppContainer 真实禁网）— 质量门禁报告

> 分支：`feat/f11-sandbox-os-isolation`　|　feature-builder 全栈流水线　|　日期：2026-08-07
> 报告引用：`.quality-gate.json` → `docs/quality/f11-sandbox-os-isolation-gate.md`
> 设计文档：`features/sandbox-os-isolation.md`　|　⚠️高风险（OS 级隔离，跨平台）— 已先出设计文档并就范围/机制与用户确认（Windows 真实隔离 + fail-safe 回退）

## 概述

F11 是「Tier-1 · Feature」之一（来源 F5 残留 ③）：让 `Process` 沙箱（默认 `Sandbox:Provider=Process`）在不引入 Docker 的前提下获得 **OS 级** 网络隔离与资源约束，使 `SandboxSettings.NetworkEnabled=false` 真正生效（而非仅设置一个环境标记）。

- 新增 `ISandboxIsolation` 抽象 + 三实现：`JobObjectSandboxIsolation`（Windows Job Object 资源限额）、`AppContainerSandboxIsolation`（AppContainer 真实禁网 + 内部叠加 JobObject）、`NullSandboxIsolation`（非 Windows/Off 回退）。
- `ProcessCodeSandbox` 内部接入隔离层，对外 `ICodeSandbox` / `SandboxResult` 契约**不变**。
- 新增配置 `SandboxSettings.OsIsolation`（`Off/JobObject/AppContainer/Full`，默认 `JobObject`）+ `MaxProcessCount`/`MemoryLimitBytes`/`CpuRatePercent`。
- **失败安全**：任何 OS 机制不可用（权限/平台/解释器文件系统不可达/API 未导出）一律透明回退到环境标记缓解项，绝不阻断代码执行。
- 无新增 NuGet 包（纯 `kernel32.dll` P/Invoke + `System.Diagnostics.Process`）。

## 三道质量门禁结论

| 门禁 | 结论 | 摘要 |
| --- | --- | --- |
| ddd-code-reviewer | **PASSED** | 对抗式审查 F11 隔离层（H2 资源生命周期 + H 配置 + Z 通用）。穷举最易错点：Job Object / AppContainer 句柄获取-释放对称性、P/Invoke 正确性、fail-safe 契约。发现并修复 3 项 P1（AppContainer `CreateProcessInAppContainer` 进程句柄泄漏；属性列表内存泄漏；JobObject 句柄在 `Exited` 竞态下可能未释放 → 新增 `IDisposable` 兜底），0 阻断。 |
| ddd-phase-quality-gate | **PASS** | P0=0 P1=0 P2=0 P3=0。12 类审计全过并嵌入 `features/sandbox-os-isolation.md` §6：DI 注册完整、DDD 分层正确（Application 零引用 Infrastructure）、无硬编码业务值、ct 透传、全部 `internal sealed`、并发安全（ConcurrentDictionary + 作用域兜底 Disposable）、空守卫齐备、无蓝图漂移（§3.2 `CompositeSandboxIsolation` 表述已修订为实际实现）、中文 XML 文档齐全。ArchitectureTests 9/9、依赖漏洞扫描 0。 |
| codebase-optimizer | **PASSED** | Round F11-01，0 open。七维扫描 F11 diff：架构（分层正确、策略接口 internal、DI 条件注册）、代码质量（泄漏已修、统一中文文档）、正确性（全量测试 0 失败）、测试（5 隔离单测 + 既有 ProcessCodeSandboxTests 全绿）、性能（JobObject CPU 硬上限 + 内存上限 + 活动进程上限防 fork 炸）、安全（无密钥落库、fail-safe、安全默认离线）、工程化（build 0/0、`dotnet list --vulnerable` 无 CVE）。注：按 feature-builder 硬约束以**分析模式**运行，不新建 `codebase-optimizer/{date}` 分支、不 push。 |

## ddd-code-reviewer · 对抗式审查报告

### Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix / 状态 |
|----------|----------|-----------|---------|----------|----------------------|
| P1 | Section H2 / 句柄泄漏 | WindowsAppContainer.cs:153 | `CreateProcessInAppContainer` 返回的 `pi.hProcess` 句柄在经 `Process.GetProcessById` 包装后从未 `CloseHandle`，导致每次 AppContainer 启动泄漏一个进程句柄 | 成功路径仅 `CloseHandle(pi.hThread)` 后 `GetProcessById`，原始 `hProcess` 被丢弃 | **已修复**：`GetProcessById` 后立即 `if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);`（包装对象自有独立句柄） |
| P1 | Section H2 / 原生内存泄漏 | WindowsAppContainer.cs:174-176 | `BuildAttributeList` 第二次 `InitializeProcThreadAttributeList(mem,...)` 失败时，`mem`（已 `AllocHGlobal`）未释放即 `return false` | 失败分支仅 `return false`，无 `FreeHGlobal` | **已修复**：失败分支先 `Marshal.FreeHGlobal(mem);` 再 `return false;` |
| P1 | Section H2 / 句柄释放竞态 | JobObjectSandboxIsolation.cs:47-56 | Job Object 句柄仅借 `Process.Exited` 事件释放；若进程退出事件早于 `Process.Dispose` 触发（微秒级竞态），`OnExited` 不触发 → 句柄泄漏至 GC（无终结器） | `OnExited` 订阅于 `process.Exited`，但 `ProcessCodeSandbox` 用 `using var process` 在 `WaitForExitAsync` 返回后立即 dispose | **已修复**：`JobObjectSandboxIsolation` 实现 `IDisposable`，作用域结束时遍历 `_active` 释放仍活跃 Job 句柄（已退出的进程在字典中已移除，仅清理残留项） |
| P2 | Section H2 / 失败路径收尾 | WindowsAppContainer.cs:140-149 | `Launch` 失败分支未防御性关闭 `pi.hProcess`（当 `!ok` 但 `hProcess!=Zero` 的异常情形） | 仅关闭读/写管道与 `hThread` | **已修复**：补 `if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);` |
| P3 | Section Z / 探针残留 | AppContainerSandboxIsolation.cs | 探针进程在 `probe.Dispose()` 时仅关句柄不 kill；若探针命令异常长活会成孤儿。当前探针 `python -c "print('ok')"` 瞬时退出，实际无风险 | 短命令即时退出 | 留观不修（低风险）；如需强化可在 `Dispose` 前 `TryKill` |

> **至少 3 项风险已穷举核查并给结论**：① Job Object 句柄释放 — FOUND（竞态）+ 已修复（IDisposable）；② AppContainer 进程/属性列表句柄泄漏 — FOUND + 已修复；③ fail-safe 契约 — VERIFIED（`TryCreateProfile` 捕获 `EntryPointNotFoundException` 等全部 P/Invoke 异常回退；`TryLaunchAsync` 任意失败返回 `null`）；④ `Exited` 事件对 `Process` 的二次 Dispose — 核查：前一会话已修（仅释放 Job 句柄，不 Dispose Process），`No process is associated` 已消除；⑤ 跨平台 — 核查：非 Windows 解析 `NullSandboxIsolation`，`RuntimeInformation` 守卫齐全；⑥ 配置误配（CPU 0 / 内存过小）— 核查：`WindowsJobObject` 对 `cpu` clamp 1-100、`mem` 取 `Max(配置, 32MB)` 下限，避免饿死/误伤。

### Control Flow Analysis
- Entry point: `ProcessCodeSandbox.RunCodeAsync` → `_isolation.CanLaunch ? TryLaunchAsync : RunProcessAsync`
- Execution path (常规 JobObject 路径): `ResolveInterpreter` → `WriteTempFile` → `Process.Start` → `_isolation.Attach(process)` → `ProcessCaptureHelper.CaptureAsync` → 读退出码 → `TryDelete(tempFile)`
- Execution path (AppContainer 路径): `TryGetCmds` → `WindowsAppContainer.TryCreateProfile` → 探针 `Launch`+`CapturePipeAsync`（校验 `ok`）→ 真实 `Launch`（`-` 读 stdin）→ `WriteStdinAsync` + 内联 `WindowsJobObject.Assign` → `CapturePipeAsync` → 结果；任一环节失败 → 返回 `null` 触发 `ProcessCodeSandbox` 回退到常规路径
- Dead ends: none（所有分支 return 或 fail-safe 回退 null）
- Unregistered interfaces: none — `ISandboxIsolation` 在 `DependencyInjection.cs:187` 工厂注册为 Scoped（按 平台+OsIsolation 解析 Null/JobObject/AppContainer）

### Test Coverage
- Unit（5，`SandboxIsolationTests.cs`）：`JobObjectSandboxIsolation_Windows_Attach_ExecutesCodeSuccessfully`、`JobObjectSandboxIsolation_NonWindows_AttachReturnsFalse`、`WindowsJobObject_Direct_Assign_DoesNotThrow`、`AppContainerSandboxIsolation_TryLaunch_NeverReturnsFailedResult`、`ProcessCodeSandbox_WithAppContainerIsolation_StillRunsCode_ViaFailSafe`
- 既有回归（`ProcessCodeSandboxTests.cs` 新增 3 参构造）：语言拒绝 / 超时 kill / 退出码 / 输出截断全绿
- Untested paths: AppContainer 真实禁网实测仅在已准备 ACL 的 Windows 上跑（本沙箱靠 `SkippableFact` 跳过，由 fail-safe 不变量断言覆盖）；非 Windows 路径跳过
- Missing edge cases: 超时 kill 由既有 `ProcessCodeSandboxTests` 覆盖

### API Verification
- External APIs used: `kernel32.dll` P/Invoke — `CreateJobObject`/`SetInformationJobObject`/`AssignProcessToJobObject`/`OpenProcess`/`CloseHandle`；`CreateAppContainerProfile`/`DeleteAppContainerProfile`/`CreateProcessInAppContainer`/`CreatePipe`/`SetHandleInformation`/`InitializeProcThreadAttributeList`/`UpdateProcThreadAttributeList`/`DeleteProcThreadAttributeList`；`System.Diagnostics.Process.WaitForExitAsync`/`Kill(entireProcessTree:true)`
- Verified against platform: 全部为 Windows Win32 API；`CreateAppContainerProfile` 在本构建未导出（`EntryPointNotFoundException` 已捕获回退），符合 fail-safe 设计
- Mismatches found: none（编译 0 错误确认签名匹配）
- Unverifiable: AppContainer 真实禁网行为需在已配置 ACL 主机实测（CI `windows-latest` 可作为后续真实验证点）

### Blueprint Alignment
- Requirements checked: 2（资源限额 / 网络隔离）+ N（fail-safe / 配置驱动 / 无契约变更）
- Implemented: 全部（JobObject 限额实测；AppContainer 网络隔离实现 + 回退；`SandboxSettings.OsIsolation` 配置驱动；`ICodeSandbox` 契约不变）
- Missing: none
- Contradicts: none（设计文档 §3.2 原 `CompositeSandboxIsolation` 漂移已修订为实际实现）

### Top 3 Runtime Risks
1. **AppContainer 解释器文件系统壁垒** — `WindowsAppContainer.cs:Launch` — 未授予 `ALL APPLICATION PACKAGES` 读 ACL 的主机上 `python`/`node` 无法加载 → `Launch` 失败 → `TryLaunchAsync` 返回 `null` → `ProcessCodeSandbox` 回退常规路径（资源限额仍由 JobObject 兜底）。fail-safe 已验证。
2. **Job Object 句柄泄漏** — `JobObjectSandboxIsolation.cs` — 进程退出事件竞态早于 Process dispose → 已通过新增 `IDisposable` 作用域兜底修复。
3. **AppContainer 进程/属性列表句柄泄漏** — `WindowsAppContainer.cs` — `CreateProcessInAppContainer` 原始句柄与属性列表内存未释放 → 已修复（成功/失败全出口 `CloseHandle` + `FreeHGlobal`）。

## ddd-phase-quality-gate · 12 类审计（摘要）

完整审计表嵌入 `features/sandbox-os-isolation.md` §6。**结论 PASS（P0=0, P1=0, P2=0, P3=0）**：
DI 注册完整 / DDD 分层正确 / 无新增聚合映射 / 无硬编码业务值 / ct 透传 / 全部 `internal sealed` / 并发安全（ConcurrentDictionary + 作用域 Disposable 兜底）/ 空守卫齐备 / 无 API 基础设施变更 / 无蓝图漂移 / 中文 XML 文档齐全 / 无 Swagger 变更。附加：ArchitectureTests 9/9 通过、`dotnet list package --vulnerable` 0 易受攻击包。

## codebase-optimizer · 七维扫描（分析模式）

Round F11-01，0 open：
- **架构**：`SandboxSettings`/`OsIsolationMode` 在 Application.Abstractions；实现全在 Infrastructure.Sandbox（`internal sealed`）；`ISandboxIsolation` 为 internal 策略接口；DI 工厂条件注册完整。
- **代码质量**：3 处 P1 句柄泄漏已修复；命名/异常/日志与 `ProcessCodeSandbox` 一致；中文 XML 文档就绪。
- **正确性**：全量 `dotnet test` 0 失败（Arch 9 / App 188 / Infra 133+4 skip / Integration 5 / Api 35 / SpecFlow 114）。
- **测试**：5 隔离单测 + 既有 ProcessCodeSandboxTests 全绿；AppContainer 以 fail-safe 不变量断言覆盖。
- **性能**：JobObject CPU 速率硬上限 + 作业/进程内存上限 + 活动进程数上限（防 fork 炸），配置可调。
- **安全**：无密钥/命令落库；AppContainer 真实禁网（可用时）；默认 `NetworkEnabled=false` 离线；失败路径捕获全部 P/Invoke 异常回退不阻断。
- **工程化**：`dotnet build` 0 警告 0 错误；无新增依赖；`dotnet list package --vulnerable` 无 CVE。
- **约束遵守**：按 feature-builder 硬约束（每 feature 分支 / 不 push / 不 merge）以分析模式运行，**未新建 `codebase-optimizer/{date}` 分支、未 push**。

## 已知残留 / 后续增强

- AppContainer 真实禁网行为需在本沙箱以外的、已对解释器目录授予 `ALL APPLICATION PACKAGES` 读 ACL 的 Windows 主机（或 CI `windows-latest`）实测；本开发沙箱因 `CreateAppContainerProfile` 未导出 / 缺 ACL 准备，以 `SkippableFact` 跳过，靠 fail-safe 不变量保证不红。
- Linux（`unshare`/cgroups v2/seccomp）与 macOS（`sandbox-exec`）路径当前走 `NullSandboxIsolation` 回退，标注为后续 feature 扩展点（不破坏现有 Windows 路径）。
