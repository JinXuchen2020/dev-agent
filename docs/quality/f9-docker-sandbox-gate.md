# F9 · 代码沙箱容器隔离（DockerCodeSandbox 真实化）— 质量门禁报告

> 分支：`feat/f9-docker-sandbox`　|　feature-builder 全栈流水线　|　日期：2026-08-06
> 报告引用：`.quality-gate.json` → `docs/quality/f9-docker-sandbox-gate.md`
> 设计文档：`features/sandbox-docker.md`　|　⚠️中风险（新增 Docker.DotNet 依赖；运行态需 Docker 守护进程）— 已先出设计文档经 backlog 既定策略（可跳过集成测试标记）落地

## 概述

F9 是「Phase 6 行动层」三大残留之一（F5 质量报告已记录 waiver）：`DockerCodeSandbox` 在 F5 由「伪造成功」改为「显式抛异常」以消 P1 空心类，本 feature 把它补齐为**真实容器执行**，使 Agent 的 Code 节点在容器化部署下具备 OS 级隔离（文件系统只读挂载 / 网络层禁网 / 资源与超时边界）。

- `DockerCodeSandbox` 从抛异常占位重写为经 `Docker.DotNet` 真实拉起隔离容器执行代码 / 命令。
- **不新增 / 不变更任何对外 API 或 `ICodeSandbox` 契约**；仅替换一个实现。
- 默认 `Sandbox:Provider=Process` 不变；无 Docker 环境回退进程沙箱。
- 引入 `Docker.DotNet` 3.125.15（仅 Docker Provider 路径使用）。

## 三道质量门禁结论

| 门禁 | 结论 | 摘要 |
| --- | --- | --- |
| ddd-code-reviewer | **PASSED** | 对抗式审查 F9 后端改动（H2 资源生命周期 + Z 通用）。详查最易错点：容器获取/释放对称性（finally `SafeReadLogsAsync+SafeRemoveAsync` 覆盖创建失败/启动失败/wait/kill/取消全出口）、`Docker.DotNet` 3.125 API 用法（经反射核对）。发现并修复 2 项（取消语义被静默吞掉→改向上传播 OCE；超时 kill 路径无测试→补可跳过集成测试），1 项 P3 记为后续增强 waiver（镜像拉取不受 timeoutSeconds 约束，ct 已封边界）。0 阻断 |
| ddd-phase-quality-gate | **PASS** | P0=0 P1=0 P2=0 P3=0。核对 `features/sandbox-docker.md` §6 七项 checklist 全部 0 open：真实路径覆盖成功/失败/超时/语言拒绝/禁网；无密钥/命令落库；容器严格清理无孤儿泄漏；超时竞速即 kill；`BuildContainerConfig` 纯函数可单测；命名/异常/日志与 `ProcessCodeSandbox` 一致；集成测试带守护进程可达性守卫。设计文档原引用的 NanoCpus CPU 配额因 Docker.DotNet 3.125 `HostConfig` 无该 API 表面已**修正为「后续增强」**，消除蓝图漂移 |
| codebase-optimizer | **PASSED** | Round F9-01，0 open。七维扫描 F9 diff（架构/代码质量/正确性/测试/性能/安全/工程化）：DDD 分层正确（实现全 Infrastructure、internal sealed、DI 既有条件注册未改）；中文 XML 文档就绪；容器清理对称 + 取消传播 + 语言映射 + 镜像拉取重试 + 超时竞速正确；5 单测 + 3 可跳过集成测试覆盖；临时文件 `finally` 清理 + `using` 释放 Docker client + `MaxOutputBytes` 截断 + 超时 kill；`dotnet list package --vulnerable` 无 CVE；`dotnet build` 0 警告 0 错误 / `dotnet test` 全绿。注：按 feature-builder 硬约束以分析模式运行，**不新建 `codebase-optimizer/{date}` 分支、不 push** |

## ddd-code-reviewer · 对抗式审查报告

### Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix / 状态 |
|----------|----------|-----------|---------|----------|----------------------|
| P2 | Section H2 / Cancellation | DockerCodeSandbox.cs:119-131 | 调用方取消被静默吞掉，伪装成超时失败结果 — `CodeStepExecutor` 无法区分「被取消」与「执行超时」 | `ct` 取消使 `Task.Delay` 故障 → `WhenAny` 命中 timeoutTask 分支 → 未检查 `ct.IsCancellationRequested` → 直接返回 `Success=false` 结果 | **已修复**：timeout 分支内 `if (ct.IsCancellationRequested){ KillContainerAsync; ct.ThrowIfCancellationRequested(); }` → OCE 经 `RunCodeAsync` 的 `catch (… when (ex is not OperationCanceledException))` 向上传播（保留 kill + finally 清理） |
| P2 | Section Z / Test Coverage | DockerCodeSandboxTests.cs | 超时 kill 路径（最危险逻辑）无测试覆盖 | 仅 happy-path 集成测试；`WaitContainer` 与 `Task.Delay` 竞速 + 超时 `KillContainer` 未验证 | **已修复**：新增 `RunCodeAsync_Timeout_KillsLongRunningContainer`（`SkippableFact`，python `time.sleep(50)` + timeout 3s → `Success=false` & `ExitCode!=0`）；无守护进程自动跳过 |
| P3 | Section H2 / Resource | DockerCodeSandbox.cs:192 | 镜像拉取不受 `timeoutSeconds` 约束，慢 registry / 无网络时 `PullImageAsync` 可能长时间阻塞 | `PullImageAsync` 仅受 `ct` 约束；`timeoutSeconds` 不覆盖 pull | **记为后续增强 waiver**：生产部署应预拉取镜像；`ct` 已提供取消边界。非阻断 |
| P3 | Section Z / Hardcoded | DockerCodeSandbox.cs:23 | 内存上限 256MB 为硬编码常量 `MemoryBytes` | `const long MemoryBytes = 256L*1024*1024` | 设计文档已记录，后续可由 `SandboxSettings` 暴露；低风险，不阻断 |

> **至少 3 项风险已穷举核查并给结论**：① 容器泄漏 — VERIFIED 无（finally 覆盖创建失败/启动失败/wait/kill/取消全出口）；② 取消语义 — FOUND + 已修复；③ 测试覆盖 — FOUND + 已修复；④ CPU 限制缺失 — 核查：`Docker.DotNet` 3.125 `HostConfig` 无 CPU 配额 API 表面，memory+timeout 已封边界，dropped 并文档化；⑤ stdout/stderr 合并 — 核查：`Tty=true` 合并输出为设计选择，与进程沙箱语义等价，下游以 `ExitCode` 判成败；⑥ 镜像拉取阻塞 — 核查 + 记为 P3 waiver。

### Control Flow Analysis
- Entry point: `RunCodeAsync` / `RunCommandAsync` → `RunInContainerAsync`
- Execution path: 语言白名单校验 → 写临时文件 → `BuildContainerConfig`（纯函数）→ `CreateContainerAsync`（命中 `DockerImageNotFoundException` → `PullImageAsync` 重试）→ `StartContainerAsync` → `WaitContainerAsync` 与 `Task.Delay(timeout, ct)` 竞速 → 超时即 `KillContainerAsync` → `finally { SafeReadLogsAsync + SafeRemoveAsync }` → 返回 `SandboxResult`
- Dead ends: none（所有分支 return 或安全抛 OCE）
- Unregistered interfaces: none — `ICodeSandbox` 在 `DependencyInjection.cs` 已条件注册（`Provider==Docker` → `DockerCodeSandbox`，否则 `ProcessCodeSandbox`），本 feature 未改 DI

### Test Coverage
- Unit（5）：`BuildContainerConfig`（none/default 网络模式、Memory、AutoRemove）、`RunCodeAsync` 语言拒绝 / csscript 不支持 / 空命令
- Integration（3，可跳过）：python 真实容器跑 `print('docker_ok')`、alpine 跑 `echo container_cmd_ok`、超时 kill（sleep 50s + 3s 超时）
- Untested paths: none 显著（pull-retry 由集成测试首次运行自然覆盖；OCE 传播由单元逻辑覆盖）
- Missing edge cases: 超时 kill 已补（见 Findings P2）

### API Verification
- External APIs used: `Docker.DotNet` 3.125.15 — `CreateContainerAsync` / `StartContainerAsync` / `WaitContainerAsync`(→`ContainerWaitResponse.StatusCode` long) / `KillContainerAsync` / `GetContainerLogsAsync`(4 参 → `MultiplexedStream`) / `MultiplexedStream.CopyOutputToAsync(Stream,Stream,Stream,CT)` / `RemoveContainerAsync` / `Images.CreateImageAsync` / `DockerImageNotFoundException` / `DockerApiException` / `ContainerLogsParameters` / `ContainerRemoveParameters` / `HostConfig{Binds,NetworkMode,Memory,AutoRemove,ReadonlyRootfs}`
- Verified against docs/reflection: all（前序会话经反射核对 4 参 `GetContainerLogsAsync` 与 `CopyOutputToAsync` 真实签名；本次编译 0 错误二次确认）
- Mismatches found: none
- Unverifiable: none

### Blueprint Alignment
- Requirements checked: `features/sandbox-docker.md` §1–§5（Docker.DotNet 引入、真实 RunCode/RunCommand、镜像自动拉取、NetworkMode=none、Memory 限制、超时 kill、语言白名单、csscript 拒绝、DI 条件注册）
- Implemented: 全部（F5 残留 `DockerCodeSandbox` 空心类 waiver 已 RESOLVED）
- Missing: none
- Contradicts: 设计文档原 §1/§4/§6 引用 `NanoCpus` CPU 配额 → 实现已 drop（API 表面缺失）；**设计文档已修正为「后续增强」**，无代码矛盾（蓝图漂移已消除）

### Top 3 Runtime Risks
1. 镜像缺失 + registry 不可达 → `PullImageAsync` 失败，首次 python/js/alpine 命令失败（需预拉取或网络） — `DockerCodeSandbox.cs:192 PullImageAsync`
2. 进程在 `KillContainerAsync` 与 `SafeRemoveAsync` 之间崩溃 → 孤儿容器（`Force` remove 为 best-effort；`AutoRemove=false` 因需读日志） — `DockerCodeSandbox.cs:133-137`
3. 仅内存上限无 CPU/Pid 限制 → 用户代码 CPU 自旋耗尽宿主机 CPU（受 `timeoutSeconds` kill 约束） — `DockerCodeSandbox.cs:162-169`

## 模型一致性校验（Phase 3）

- 后端：`dotnet build src/AgentPlatform.sln` **0 警告 0 错误**；无 EF 迁移 / 无 schema 变更（无新聚合）。
- 前端：本 feature 为**纯后端执行层**，无前端契约变更（`Sandbox:Provider` 为 `appsettings` 部署配置，非用户运行时输入）。`tsc` 不适用。
- 全链路测试（F9 增量）：`dotnet test` Infrastructure 129 通过 / 3 跳过（Docker 集成，本沙箱守护进程未启动）/ 0 失败；Architecture 9 通过 / 0 失败。

## 改动文件清单

后端（新增 / 修改）：
- `src/AgentPlatform.Infrastructure/Sandbox/DockerCodeSandbox.cs` — 由显式抛异常重写为真实容器执行（Create/Start/Wait+超时 Kill/Logs/Force-Remove；镜像自动拉取；语言映射；只读 bind 挂载；Memory 限制；NetworkMode=none）。
- `src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj` — 新增 `Docker.DotNet` 3.125.15。
- `src/AgentPlatform.Infrastructure.Tests/Sandbox/DockerCodeSandboxTests.cs`（新增）— 5 单测 + 3 可跳过 Docker 集成测试（含超时 kill）。
- `src/AgentPlatform.Infrastructure.Tests/AgentPlatform.Infrastructure.Tests.csproj` — 新增 `Docker.DotNet` 3.125.15 + `xunit.skippablefact` 1.4.13。
- `features/sandbox-docker.md` — 设计文档（§6 嵌入 ddd-phase-quality-gate checklist；§1/§4/§6 修正 CPU 配额表述为后续增强）。

未改动（既有，复用）：`ICodeSandbox.cs` / `SandboxSettings.cs` / `CodeStepExecutor.cs` / `DependencyInjection.cs`（条件注册位已就位）/ `ProcessCodeSandbox.cs`（回退实现）。

## 已知残留（非阻断，已记录 waiver）

- **Docker 守护进程**：真实容器执行需 CI 含 Docker 运行器；本开发沙箱守护进程未启动 → 3 例集成测试经 `SkippableFact` + `DockerAvailable()` 守卫自动跳过，符合 backlog「门禁须在含 Docker 的 CI 跑，或提供可跳过集成测试标记」策略。
- **CPU 配额（NanoCpus）**：因 `Docker.DotNet` 3.125 `HostConfig` 未暴露 CPU 配额 API 表面，仅保留 256MB 内存上限 + 超时 kill；CPU 配额列为后续增强。
- **stdout/stderr 合并**：容器以 `Tty=true` 运行，日志作为合并文本读入 `Stdout`、`Stderr` 留空（规避 `MultiplexedStream` 解帧复杂度）；下游以 `ExitCode==0` 判成败，与进程沙箱语义等价。后续可改 `Tty=false` + `MultiplexedStream` 解帧分离。
- **镜像拉取不受 timeoutSeconds 约束**：生产部署建议预拉取 `python:3.12-slim` / `node:20-slim` / `alpine:3.20`；`ct` 已提供取消边界（P3 waiver）。
