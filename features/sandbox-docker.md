# F9 · 代码沙箱容器隔离（DockerCodeSandbox 真实化）

> 状态：`done`（feature-builder 全栈流水线；分支 `feat/f9-docker-sandbox`；三道质量门全 PASS，见 `docs/quality/f9-docker-sandbox-gate.md`）
> 优先级：P2 · 风险：⚠️中风险（新增 Docker.DotNet 依赖；运行态需 Docker 守护进程）
> 来源：F5 残留 ① —— `DockerCodeSandbox` 现为显式抛异常占位，本 feature 补真实容器执行。

## 1. 目标

在配置了 `Sandbox:Provider=Docker` 的环境，用 `Docker.DotNet` 真实拉起**隔离容器**执行用户代码 / 命令，提供比进程沙箱（`ProcessCodeSandbox`）更强的文件系统 / 网络 / 资源边界：

- 代码在一次性容器内运行，执行完即销毁，宿主机文件系统不暴露（仅挂载代码文件为只读）。
- 默认 `NetworkEnabled=false` → 容器 `NetworkMode=none`，真正在容器网络层禁网。
- 资源上限：内存（256 MB）、输出截断（`MaxOutputBytes`）、超时 kill。
- 默认 `Provider=Process` 不变；无 Docker 环境自动回退进程沙箱，保证可运行。

> **实现注记（CPU 限制）**：设计初稿曾拟 `NanoCpus=1 核`，但 `Docker.DotNet` 3.125 的 `HostConfig` 在本版本未暴露 CPU 配额（NanoCpus/CpuPeriod/CpuQuota 实际位于嵌套 `Resources`，构造不稳定）。最终实现**仅保留 256MB 内存上限 + 超时强制 `KillContainer`** 作为边界：超时 kill 已能终止 CPU 自旋类负载，内存上限防 OOM。CPU 配额列为后续增强。

这是「Phase 6 行动层」三大残留之一，补齐后 Agent 的 Code 节点在容器化部署下具备 OS 级隔离。

## 2. 接口契约

**本 feature 不新增 / 不变更任何对外 API 或 `ICodeSandbox` 契约**。仅替换 `ICodeSandbox` 的一个实现（`DockerCodeSandbox` 由抛异常改为真实执行）。契约维持：

```csharp
// AgentPlatform.Application.Abstractions.ICodeSandbox（不变）
Task<SandboxResult> RunCodeAsync(string code, string language, int timeoutSeconds = 30, CancellationToken ct = default);
Task<SandboxResult> RunCommandAsync(string command, int timeoutSeconds = 30, CancellationToken ct = default);

public record SandboxResult(bool Success, string Stdout, string Stderr, int ExitCode, long DurationMs);
```

**前端契约**：无（纯后端执行层，UI 不可见）。`Sandbox:Provider` 为 `appsettings` 部署配置，非用户运行时输入。

**配置**：
- `Sandbox:Provider` = `Docker` | `Process`（默认 `Process`）。
- `Sandbox:NetworkEnabled` / `TimeoutSeconds` / `MaxOutputBytes` / `AllowedLanguages` 复用既有 `SandboxSettings`，语义不变。

## 3. 数据模型

无新聚合 / 无 EF 迁移 / 无新表。`DockerCodeSandbox` 为无状态执行器，运行态仅依赖宿主机 Docker 守护进程与临时文件。

## 4. 验收标准

- [ ] 引入 `Docker.DotNet`（`AgentPlatform.Infrastructure`）。
- [ ] `DockerCodeSandbox` 真实实现：
  - `RunCodeAsync`：按语言映射镜像（`python:3.12-slim` / `node:20-slim`）→ 写临时文件 → 只读 bind 挂载 `/sandbox/code.<ext>` → `CreateContainer` → `StartContainer` → `WaitContainer`（带超时，超时 `KillContainer`）→ 读 `GetContainerLogs` 捕获 stdout/stderr/ExitCode → `RemoveContainer(Force)` 清理。
  - `RunCommandAsync`：以 `alpine:3.20` 运行 `sh -c <command>`，其余同。
  - 镜像缺失时 `Images.CreateImageAsync` 拉取；拉取失败（无网络）→ 清晰失败信息，不静默假成功。
  - 资源限制（Memory=256MB / NetworkMode=none / 输出截断 / 超时 kill）落实；CPU 配额列为后续增强（见 §1 实现注记）。
  - 语言白名单（`AllowedLanguages`）外 → 拒绝；`csscript` 在 Docker 模式不支持 → 清晰失败。
- [ ] `Sandbox:Provider=Docker` 时 DI 条件注册切到真实 `DockerCodeSandbox`（既有 `DependencyInjection.cs` 注册位已就位）。
- [ ] 单元测试（`DockerCodeSandboxTests`）：
  - **纯函数单测（无需守护进程）**：`BuildContainerConfig` 断言镜像名 / Cmd / Binds / NetworkMode / Memory / NanoCpus / AutoRemove 正确（python / javascript / 禁网 / 语言拒绝）。
  - **集成单测（需守护进程）**：真实拉起容器跑 `print('ok')` 并断言 `Success && ExitCode==0 && Stdout` 含 `ok`；守护进程不可达时经 `SkipException` 跳过（本开发沙箱无 Docker 守护进程 → 自动跳过，符合 backlog 既定策略「门禁须在含 Docker 的 CI 跑，或提供可跳过集成测试标记」）。
- [ ] `dotnet build` 0/0；`dotnet test src/AgentPlatform.sln` 全绿（既有测试不回归；Docker 集成测试在本沙箱跳过）。

## 5. 风险点

- **新依赖**：`Docker.DotNet` 引入第三方客户端；仅用于 Docker Provider，不影响默认 Process 路径与无 Docker 部署。
- **运行态依赖**：真实容器执行需 Docker 守护进程。本开发沙箱守护进程未启动 → 集成测试跳过；CI 需含 Docker 的运行器才能覆盖真实路径。
- **stdout/stderr 合并**：为规避 `MultiplexedStream` 解帧复杂度与跨版本差异，容器以 `Tty=true` 运行，日志作为合并文本读入 `Stdout`，`Stderr` 留空。下游 `CodeStepExecutor` 以 `ExitCode==0` 判成功、以合并输出兜底错误文本（与进程沙箱语义等价，仅 stderr 单列信息缺失）。**后续增强**：可改为 `Tty=false` + `MultiplexedStream` 解帧以分离 stdout/stderr。
- **镜像拉取网络**：`python:3.12-slim` / `node:20-slim` / `alpine:3.20` 需 Docker Hub 可达或预拉取；拉取失败显式报错。
- **安全边界**：代码文件只读挂载，容器无宿主机权限；`NetworkEnabled=false` 时 `NetworkMode=none` 真正禁网；超时强制 `KillContainer` 防挂起；输出截断防上下文撑爆。

## 6. 质量门禁 Checklist（ddd-phase-quality-gate 嵌入）

- [ ] **P0** `DockerCodeSandbox` 不再抛「未接入」异常；真实路径覆盖成功 / 失败 / 超时 / 语言拒绝 / 禁网。
- [ ] **P0** 密钥 / 命令不落库、不写日志；失败信息不含敏感路径外泄。
- [ ] **P1** 容器严格清理（finally 中 `RemoveContainer(Force)`），无孤儿容器泄漏。
- [ ] **P1** 超时路径：wait 与 timeout 竞速，超时即 `KillContainer` 且 `Success=false` + 提示超时。
- [ ] **P2** `BuildContainerConfig` 纯函数可单测，命令构造与 Process 沙箱语义对齐（语言映射 / 白名单 / 禁网）；资源限制以 Memory(256MB)+超时 kill 为主，CPU 配额（NanoCpus）因 Docker.DotNet 3.125 API 表面未暴露而列为后续增强。
- [ ] **P2** 命名 / 异常 / 日志与 `ProcessCodeSandbox` 风格一致；无静默假成功。
- [ ] **P3** 集成测试带守护进程可达性守卫，无守护进程时跳过而非失败。
