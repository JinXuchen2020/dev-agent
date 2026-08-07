# 进程沙箱 OS 级隔离：与主流 Agent Harness 产品的设计对比

> 状态：**已定稿（分析文档，非实现变更）**
> 关联：`features/sandbox-os-isolation.md`（F11 设计）、`features/backlog.md`（F9 Docker 真实化）、`docs/agent-harness-blueprint.md`
> 背景：F11 已在 `feat/f11-sandbox-os-isolation` 分支把 `ProcessCodeSandbox` 升级为 Windows JobObject 资源限额 + AppContainer 真实禁网（fail-safe 回退）。本文把这套实现放在"业界 agent 代码执行沙箱"的坐标系里，说明它的定位、取舍，以及和 E2B / OpenAI Code Interpreter / Docker / gVisor / Claude Code 本地模式的本质差异，并给出收敛差距的建议。

---

## 1. 一句话结论

**F11 是"同内核 OS 原语"隔离（最轻、Windows 原生、零新依赖）；主流 harness 是"VM 边界"隔离（最安全、但重、需 Linux/KVM 或云基础设施）。** 两者没有谁错，是部署模型与威胁模型不同导致的取舍：我们是单租户 / 自托管 dev 环境，云厂商是多租户云。

---

## 2. 隔离边界拓扑

### 2.1 我们的模型（同内核，无 VM 墙）

```
┌─────────────────────────────────────────────┐
│ Host OS · Windows kernel                      │
│                                               │
│   ┌───────────────────────────────────────┐  │
│   │ Sandbox process (python / node)        │  │  ← 与宿主共享同一内核
│   │   ├─ JobObject   : mem / CPU 速率 / 进程数 │  │
│   │   └─ AppContainer: 无 internetClient 能力 │  │
│   └───────────────────────────────────────┘  │
│   （虚线 = 软边界：无 VM 墙，攻击面共享）       │
└─────────────────────────────────────────────┘
```

### 2.2 主流 harness 模型（VM 边界，宿主受硬件保护）

```
┌─────────────────────────────────────────────┐
│ Host · hypervisor / cloud                      │
│                                               │
│   ┌───────────────────────────────────────┐  │
│   │ Guest VM (Firecracker / KVM)  ← 硬墙   │  │  ← 实线红 = 硬件隔离边界
│   │   ├─ Guest kernel                      │  │
│   │   └─ Sandbox process                   │  │
│   └───────────────────────────────────────┘  │
│   （宿主内核与客体内核物理分离 → 逃逸防护强）  │
└─────────────────────────────────────────────┘
```

**关键差异**：VM 把"客户内核"和"宿主内核"分开，恶意脚本即使打穿客户内核也到不了宿主；我们的进程和宿主跑在同一个 Windows 内核里，内核级漏洞理论上仍可横向移动。

---

## 3. 五类原型逐维度对比

| 维度 | **dev-agent F11（我们）** | E2B / OpenAI 式 | Docker 容器 | gVisor | Claude Code 本地（无沙箱） |
|------|--------------------------|----------------|-------------|--------|----------------------------|
| 隔离边界 | 同内核（JobObject+AppContainer） | 硬件 VM（Firecracker/KVM） | 共享内核 namespace | 用户态内核 | 无（信任用户机） |
| 防宿主机逃逸 | **弱**（同内核攻击面） | **强** | 中（容器逃逸风险类） | 强 | 无 |
| 真实禁网 | AppContainer 能力剥夺（**需宿主 ACL 准备，否则 fail-safe 降级**） | VM 级网络策略（确定性生效） | `NetworkMode=none` | syscall 拦截 | 无 |
| 资源限额 | JobObject（内存/CPU 速率/进程数） | VM 级 cgroup | cgroup | 拦截+限额 | 无 |
| 启动开销 | **极低**（进程级，无 daemon） | 中（~100-200ms） | 中 | 中 | 无 |
| 跨平台 | **Windows 优先**（Linux/macOS 走 Null 回退） | 必需 Linux/KVM | 需 Docker daemon | Linux | 任意 |
| 新依赖 | **无**（纯 `kernel32` P/Invoke） | 云基础设施 | Docker | runsc | 无 |
| 失败策略 | **fail-safe 开放**（降级不阻断执行） | fail-closed（隔离建不起来就拒执行） | fail-closed | fail-closed | N/A |
| 多租户安全 | 不面向（单租户/自托管） | 面向 | 视部署 | 面向 | 不面向 |

---

## 4. 防护强度 vs 开销（定性评分 0–10）

| 原型 | 宿主机防护强度 | 资源/启动开销 |
|------|---------------:|--------------:|
| microVM（E2B / OpenAI） | 9 | 7 |
| gVisor | 8 | 5 |
| Docker 容器 | 6 | 4 |
| **Our JobObject + AppContainer** | **4** | **2** |
| 无沙箱（Claude Code 本地） | 1 | 1 |

> 我们的实现位于"最轻、防护最低"的极端：代价最小、零依赖、Windows 原生；但缺少 VM 级逃逸防护。

---

## 5. 五点关键差异

1. **隔离粒度相反**：我们用 JobObject/AppContainer 把进程"关"在同一个 Windows 内核里；E2B/OpenAI 用 Firecracker 把客户内核与宿主内核物理分开。VM 级逃逸防护是防护强度的根本来源。

2. **失败策略相反（最重要）**：我们是 **fail-safe 开放**——任何 OS 机制建不起来（AppContainer 在该 Windows 版本未导出、解释器目录没配 ACL），就静默降级成"环境标记 + 语言白名单 + 超时杀 + 输出截断"，**绝不挡住代码执行**。云厂商是 **fail-closed**——隔离建不起来就拒绝执行，因为他们是多租户，不能让代码裸跑。根因：我们是单租户/自托管 dev，"跑不起来"比"弱隔离"更糟。

3. **Windows-first、零新依赖**：别人基本 Linux/KVM 或 Docker daemon 优先；我们针对"dev 沙箱是 Windows、无 Docker"的现状，纯 `kernel32` P/Invoke，不引入 Firecracker/Docker/gVisor 任何东西。

4. **真实禁网有前提代价**：AppContainer 真禁网要求宿主对 `python`/`node` 目录**预先授予 `ALL APPLICATION PACKAGES` 读 ACL**，否则进程根本起不来。我们 fail-safe 处理了这个——但副作用是：**未准备的主机上其实没有真禁网**，只是降级。这是和 VM 网络策略（确定性生效）的关键落差，也是 F11 把 AppContainer 启动写成 `SkippableFact`、靠 fail-safe 不变量保证测试不红的原因。

5. **无文件系统写隔离 / 无 syscall 过滤**：VM/gVisor 有（read-only rootfs、seccomp）；我们 JobObject 完全不管文件，AppContainer 有部分文件能力控制但同样依赖 ACL 准备。

---

## 6. 我们的实现：好在哪、弱在哪

**好**
- 极轻、Windows 原生、零依赖、不阻断执行。
- 直接嵌进现有 `ProcessCodeSandbox`，对外 `ICodeSandbox` / `SandboxResult` 契约不变。
- 适合"我自己的机器 / 自托管上跑 agent 代码"的单租户场景。

**弱**
- 同内核攻击面（无 VM 级逃逸防护）。
- 真实禁网依赖主机 ACL 准备，未准备则降级为空隔离。
- Linux / macOS 当前退化成无隔离（`NullSandboxIsolation`）。

---

## 7. 收敛差距的建议（两层并存）

要 VM 级防护，需引入 **Firecracker/KVM（要 Linux 宿主或云）或 Docker**（F9 已把 `DockerCodeSandbox` 真实化：`NetworkMode=none` + 资源限额 + read-only rootfs + seccomp）。

**推荐架构：两层并存，确定性优先**

```
执行请求
  └─ 检测可用隔离层（按配置优先级）
       ├─ Docker 可用  → DockerCodeSandbox（NetworkMode=none + cgroup + seccomp + ro-rootfs）★ 确定性隔离
       └─ 无 daemon    → ProcessCodeSandbox + ISandboxIsolation（JobObject + AppContainer）★ 轻量兜底
                         └─ AppContainer 不可建 → fail-safe 降级 + 显式告警"隔离 weaker"
```

要点：
- **默认走 Docker/VM**，让隔离"确定性生效"，消除 AppContainer ACL 准备的脆弱性。
- **无 Docker 时降级到 JobObject+AppContainer**，并**明确告知用户当前隔离 weaker**（不静默假装强隔离）。
- `ISandboxIsolation` 抽象已就位，扩展 `DockerSandboxIsolation` 只需新增一个实现并在 `DependencyInjection` 里按可用性与配置解析，无需改动 `ProcessCodeSandbox` 主路径。

---

## 8. 决策建议 / 下一步

- **短期（已具备）**：F11 的轻量兜底路径可直接用于单租户自托管，文档已消除"仅设环境标记"的旧表述漂移。
- **中期（建议）**：实现 `DockerSandboxIsolation` 作为默认强隔离层，F11 路径退化为 fallback；这同时闭合 backlog 中"真实禁网确定性生效"的残留。
- **长期（可选）**：Linux `unshare`/cgroups v2/seccomp、macOS `sandbox-exec` 的 `ISandboxIsolation` 实现，替换当前的 `NullSandboxIsolation` 回退。

---

## 9. 参考

- `features/sandbox-os-isolation.md` — F11 设计文档（含 12 类质量门审计）
- `docs/quality/f11-sandbox-os-isolation-gate.md` — F11 三道质量门报告
- `features/backlog.md` — F9（Docker 真实化）、F11（进程沙箱 OS 隔离）、F12（Tool/Code 节点全链路 e2e）
- `docs/agent-harness-blueprint.md` — Agent 运行时整体蓝图（Phase 7 Durable Execution 起）
