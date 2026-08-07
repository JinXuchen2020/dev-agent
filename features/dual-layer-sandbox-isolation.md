# F34 · 沙箱双层隔离（Docker 默认强隔离 + JobObject/AppContainer 兜底）

> 状态：**planned（已立项置顶，待 feature-builder Phase 0 补全）**
> 优先级：**[P0 最高优先级]** ｜ 风险：**⚠️中风险**（跨 F9/F11 集成；Docker 可用性探测 + 模式选择）
> 所属：Tier 1（feature-builder 消费单元）｜ 置顶：feature-builder 下一个

## 1. 立项动机

来自 `docs/sandbox-isolation-harness-comparison.md` §7「收敛差距建议」：

- F11 的 `JobObject` + `AppContainer` 是**同内核 OS 原语**隔离，失败策略为 **fail-safe 开放**——任何 OS 机制建不起来就降级成环境标记缓解项，**绝不阻断代码执行**。真实禁网依赖宿主对解释器目录预先授予 `ALL APPLICATION PACKAGES` 读 ACL，否则 AppContainer 路径透明回退、无真禁网。
- 主流 harness（E2B / OpenAI）用 **VM 边界**（Firecracker/KVM）做确定性隔离，失败策略为 **fail-closed**。
- 差距：F11 在「无 Docker 的 Windows dev 机」上获得轻量隔离，但宿主安全是同内核弱隔离；要让隔离**确定性生效**（VM 级网络策略 + read-only rootfs + seccomp），应引入 Docker/gVisor。

**结论（本 feature 采纳）**：**两层并存**——默认走 Docker 强隔离，无 daemon 时降级到 F11 的 JobObject/AppContainer 兜底，并显式告知用户「隔离 weaker」。这正与 F9（Docker 真实化）和 F11（进程级兜底）的既有分工一致。

## 2. 目标

- 在现有 `ISandboxIsolation` 抽象（F11 引入）上新增 `DockerSandboxIsolation`：`Provider=Docker` 且守护进程可用时**默认启用**，获确定性强隔离。
- `ProcessCodeSandbox` 按 `Sandbox.Provider` / `OsIsolation` 选择隔离层；对外 `ICodeSandbox` / `SandboxResult` 契约**不变**（可能新增一个只读「隔离强度」字段供可观测，属非破坏性扩展）。
- 无 daemon / `Provider=Process` / `OsIsolation=Off` 时，明确回退 F11 `JobObjectSandboxIsolation` / `AppContainerSandboxIsolation`，并打结构化日志 + 响应字段声明隔离强度（strong / weak）。

## 3. 复用点（不重复造轮子）

- **Docker 执行路径**：直接复用 F9 已真实化的 `DockerCodeSandbox`（`Docker.DotNet` 3.125.15；`NetworkMode=none` + 资源限额 + read-only rootfs + seccomp；`SafeReadLogsAsync` 的 tty 参数已在 CI 回归中修正为 `true`）。本 feature 不重写容器执行，只新增「模式选择 + 探测 + 兜底链路 + 显式告警」。
- **进程级兜底**：复用 F11 的 `JobObjectSandboxIsolation`（Windows Job Object 资源限额）+ `AppContainerSandboxIsolation`（AppContainer 真实禁网）+ `NullSandboxIsolation`（非 Windows/Off）。

## 4. 方案要点（待 Phase 0 细化）

- `SandboxSettings` 扩展：`Provider` 语义升级为「`Docker` 优先探测，失败时回退 `Process`」；新增可选 `IsolationStrength` 声明（auto / strong / weak）用于显式告知。
- 启动时一次 Docker 守护进程可用性探测（ping `/_ping` 或 `DockerClient.PingAsync`）；探测失败 → 记告警 + 走 F11 路径，不抛异常（fail-safe）。
- `ISandboxIsolation` 解析工厂：按 OS + `Provider` + 探测结果选择 `DockerSandboxIsolation` 或 `JobObjectSandboxIsolation` / `AppContainerSandboxIsolation` / `NullSandboxIsolation`。
- 结果增强：`SandboxResult` 增加非破坏性 `IsolationStrength` 字段（strong=Docker 容器 / weak=F11 进程级），供调用方与日志观测。

## 5. 验收标准（待 Phase 0 补全）

- [ ] `dotnet build` 0 错误 / 0 警告；全量 `dotnet test` 0 失败。
- [ ] Docker 守护进程可用 → `Provider=Docker` 真实容器执行，断言 `NetworkMode=none` 生效（容器内 socket 连接外部失败）、资源限额生效、rootfs read-only、seccomp 应用。
- [ ] Docker 不可用 / `Provider=Process` → 走 F11 JobObject（默认）+ 可选 AppContainer，行为与 F11 既有测试一致；响应/日志显式标注隔离强度为 weak。
- [ ] 启动时一次 Docker 可用性探测；探测失败不抛异常，静默降级并告警（fail-safe）。
- [ ] 单测：Docker 路径 `SkippableFact`（本沙箱无 daemon，跳过；CI `ubuntu-latest` 实测）；F11 兜底路径全绿。
- [ ] 文档：同步 CHANGELOG / BLUEPRINT / appendices；对比文档 §7「收敛差距建议」标记已立项。

## 6. 风险

- **中**：跨 F9/F11 集成，Docker 探测时机与失败回退链需严谨，避免探测阻塞启动或回退路径与 F11 行为漂移。
- 缓解：探测设超时 + try/catch 全包；回退路径直接复用 F11 已验证实现；Docker 路径 `SkippableFact` 不污染本沙箱测试。

## 7. 参考

- `docs/sandbox-isolation-harness-comparison.md`（§7 收敛差距建议 —— 本 feature 的动机来源）
- `features/sandbox-os-isolation.md`（F11 设计文档）
- `features/sandbox-docker.md`（F9 设计文档）
- `docs/quality/f11-sandbox-os-isolation-gate.md`、`docs/quality/f9-docker-sandbox-gate.md`
- `src/AgentPlatform.Infrastructure/Sandbox/`（F11 隔离层 + F9 DockerCodeSandbox）
