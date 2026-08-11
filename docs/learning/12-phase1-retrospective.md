# 12. 一期复盘：学习要点与踩坑实录

> 目标：一期（Tier 1，共 **29 个 feature 史诗全部 done**：F1–F34，含 F8 Negotiation 产品化、F12 Tool/Code 全链路 e2e）收官，把分散在 `01`–`11` 的学习要点收拢成"该记住的几件事"，并把收尾期新坑（#36–#47，见 `06` §6.14）归拢成一张地图。
>
> 这是一份**总索引 + 提炼**，不是重复造轮子：概念细节看对应章节，逐坑排查看 `06-common-pitfalls.md`。

---

## 一、学习要点（该记住的几件事）

按主题，每条：**一句话** → **为什么重要** → **落点/参考**。

### 1. 演进纪律：先骨架、再逻辑、后 UI、补接地、补安全、做亮点
- Phase 1 全 Stub 验证架构 → Phase 2 填真实逻辑 → **Phase 3 接口稳定后才写前端**（避免前端反复重写）→ Phase 4 把"声称完成实为存根"的能力落地 → Phase 5 安全（launch-blocking）→ Phase 6 亮点。
- 为什么重要：顺序错了会反复返工。前端最贵，必须等 API 稳定。
- 落点：`07-project-evolution.md` §7.2–§7.6。

### 2. DDD 不是装饰：聚合根 / 值对象 / 领域事件
- 聚合根 `private set`、值对象用 `record`、领域事件经适配器发出（不要直接 `mediator.Publish`）。
- 为什么重要：富领域模型才能把业务规则锁在域内，而不是散落到应用层/控制器。
- 落点：`01-ddd-in-practice.md`。

### 3. 整洁架构靠"依赖向内" + 架构测试兜底
- Domain 零 PackageReference；接口定义在 `Application.Abstractions`；Application 不引用 Infrastructure。
- 为什么重要：**架构违规编译不报错**，靠 `ArchitectureTests` 自动拦截（否则迟早双份定义、依赖反向）。
- 落点：`02-clean-architecture.md`、`06` #5/#6/#8。

### 4. EF Core 映射别靠约定
- 每个聚合写 `IEntityTypeConfiguration`；只读集合 `UsePropertyAccessMode(Field)`；`OwnsMany` 影子主键 `ValueGeneratedOnAdd()`；值对象列 `HasColumnName()` 显式指定。
- **两条铁律**（高频坑）：
  - 迁移是 schema 唯一真相：有迁移就全程 `MigrateAsync`，**禁 `EnsureCreated` 混用**（#31）。`TreatWarningsAsErrors` 下 EF 生成文件要 `#pragma warning disable IDE0161`。
  - **客户端预置 Guid 主键必须 `ValueGeneratedNever()`**，否则 `SaveChanges` 把 INSERT 当 UPDATE 报 `DbUpdateConcurrencyException`（#37）。
- 落点：`04-ef-core-aggregates.md`、`06` #9/#10/#11/#31/#37。

### 5. MediatR / CQRS：Command 自动存盘，Query 不存
- `UnitOfWorkBehavior` 管道自动 `SaveChanges`，**Command handler 勿手动 Save**；Query 不触发存盘。
- 落点：`03-mediatr-cqrs.md`。

### 6. 测试金字塔：架构测试兜底 → BDD 验行为 → 集成验真依赖
- ArchTests 拦架构违规；BDD（Reqnroll 114 + playwright-bdd 22）验业务行为；集成测试验真实 DB/沙箱依赖。
- **两个诊断陷阱**（收尾期血泪）：
  - 测试宿主进程会**静默吞掉文件写**（`File.WriteAllText` 不落地）→ 诊断靠"断言失败抛 Exception 带 dump"或 `dotnet test > file.txt` 重定向（#38）。
  - 改 bug 后别用 `dotnet test --no-build`，会跑陈旧 DLL → 务必 `dotnet test` 强制重编（#39）。
- 落点：`05-testing-strategy.md`、`06` #38/#39。

### 7. 多租户 = Query Filter + 请求解析
- `ITenantScoped` 实体挂 `HasQueryFilter`；`TenantProvider` 按请求解析租户。`AgentRoleDefinition`/`WorkflowTemplate` **故意不实现** `ITenantScoped`（平台级共享定义）。
- 为什么重要：多租户隔离是"小而高杠杆"的改动，但漏掉就会串数据。
- 落点：`10-phase5-security-learnings.md`。

### 8. Agent 编排内核：已知边界要心里有数
- 同步编排器同 HTTP 请求跑完、有状态可中断（HITL Paused/Resume），但**缺 durable/分布式执行层**；`WorkflowScheduler` 仅轮询触发器。
- **已知缺陷**：Agent 是配置实体非运行时进程，`AgentCallStepExecutor` 硬编码 prompt + `DefaultModelId`，**忽略 agent 的 `SystemPrompt`/`ModelEndpoint`**；模型路由 `ModelRouter`+`TenantModelClientResolver` 存在但未接 agent 级。
- 协商式 = `NegotiationOrchestrator`（LLM 选步）+ `CriticStepExecutor`(StepType.Critic=4) + `CriticConvergenceTermination`（Approved 或 MaxRounds=20 终止）；图含 Critic 由 `DetectPreset` 自动判 Negotiation。
- 落点：内核现状（`MEMORY.md`）、`features/negotiation-productization.md`。

### 9. 模型一致性铁律：枚举一律 int 收发
- API 全局**未注册** `JsonStringEnumConverter`，`OrchestrationPreset` 以 **int** 收发（Negotiation=1 / Sequential=0 / auto 省略由 `DetectPreset` 识别）。
- 为什么重要：前端若用字符串 JSON 会反序列化失败或落默认 → "协商模式选了却按顺序跑"（#41）。这是 F8 最易踩的坑。
- 落点：`06` #41、`features/negotiation-productization.md`。

### 10. 前端工程化三件套
- i18n 双语言（zh-CN/en-US，i18next）；BDD E2E（playwright-bdd 9.x：`testDir=defineBddConfig()`、`workers:1`+`fullyParallel:false`，逻辑变量置 `defineConfig` 外，#46）；lint `qa.mjs` 须 `--legacy-peer-deps`（#47）。
- 落点：`06` #46/#47。

### 11. 安全是 launch-blocking，编译过 ≠ 能跑
- 认证/多租户/RBAC/限流/提示注入防护/审计/API Key AES-256-GCM。
- 为什么重要：安全代码"接线后必须运行时实测"——收尾踩过 `no DefaultChallengeScheme`、Swagger 缺 Authorize、`EnsureCreated`/`Migrate` 混用 `no such table`（#27–#31）。
- 落点：`10-phase5-security-learnings.md`、`06` #27–#31。

---

## 二、踩坑地图（按主题速查）

> 一张表定位大方向，精确到坑号看 `06-common-pitfalls.md`（#1–#35 历史坑 + §6.14 #36–#47 收尾坑 + §6.15 #48–#49 E2E 纪律坑）。

| 主题 | 典型症状 | 先查 | 代表坑号 |
|------|----------|------|----------|
| 编译错 | 类型/API 找不到 | 版本号 & using | #1–#4 |
| 架构违规 | 编译不报、运行怪 | ArchitectureTests | #5/#6/#8 |
| EF 映射 | 写不进 / 并发异常 | 配置类 / 主键生成 | #9/#10/#11/#37 |
| 迁移 | 运行时缺表 | EnsureCreated/Migrate 混用 | #31 |
| 编排标志 | 工作流"假完成" | 标志是否持久化 | #36 |
| DI | 服务不生效 | 注册 + 空测试验证 | #7/#13/#15 |
| 并发 | 数据不准/偶发炸 | lock / ConcurrentDictionary | #14/#21/#22/#23 |
| 认证 | challenge 炸 / Swagger 无按钮 | 默认方案 / SecurityDefinition | #27–#30 |
| 测试诊断 | 无输出 / 改后还绿 | 宿主吞写 / --no-build | #38/#39 |
| 终态断言 | 误判全成功 | 控制标记排除 | #40 |
| 模型一致性 | 模式/枚举不生效 | int vs string 收发 | #41 |
| CI/环境 | push 失败 / PR 不触发 | 出站网络 / master 分支 | #42/#44 |
| 质量门 | commit 被拒 | .quality-gate.json 同暂存 | #45 |
| 前端工具链 | lint/test 配置炸 | peer-deps / bdd 配置 | #46/#47 |
| E2E/运行时 | strict mode / 元素找不到 / 终态等不到 | **先读真实 DOM**、后端是否可用、是否跨测试留数据 | #48/#49 |

**收尾期最值钱的三课**（#36–#47 提炼）：
1. **编排行为相关的布尔/标志字段必须落库**（如 `IsDag`）——"重跑即复位"会制造 `Completed` 假完成的静默故障（#36）。
2. **测试诊断别信落盘文件、改 bug 后别用 `--no-build`**（#38/#39）。
3. **前端与 API 的枚举/模式必须对齐序列化方式**（int vs string），否则功能"看起来选了却没生效"（#41）。

> **质量门禁盲区 vs E2E 层（#48/#49 提炼的元教训）**：三道质量门禁（`ddd-code-reviewer` / `ddd-phase-quality-gate` 是 **.NET DDD 静态审查**，`codebase-optimizer` 是**通用静态代码分析**）本质是 **pre-merge 代码形态契约闸**——读源码、查结构/类型/secret/架构分层，**不起后端、不开浏览器、不执行**。而 #48（前端 E2E 越界断言后端 LLM 收敛终态）、#49（列表卡片定位器对真实 DOM 假设错误）都是**运行期/行为期**错误，只有整套栈跑起来、且存在多卡/无 LLM 等运行时状态时才暴露。即使请人肉 reviewer，也要跨文件读 `EntityCardGrid.tsx`/`Card.tsx` + 推理测试间数据污染才能抓出。**结论：质量门禁 ≠ 测试套件——门禁防"形状"回归，E2E 防"行为"回归；E2E 的测试隔离/选择器健壮性天然属于 CI 层，不该、也没法塞进 pre-commit 代码审查。** 想收窄盲区，应在 `codebase-optimizer` 加"前端 E2E 专项"（扫 `*.steps.ts` 定位器反模式、要求 `data-testid`），而非膨胀现有三门。

---

## 三、工程实践清单（Do / Don't）

### Do（守住这些）
- 改模型/聚合 → 必 `dotnet ef migrations add`（迁移是 schema 唯一真相）。
- 新 feature → 先在 `features/` 写设计文档，再进实现（先文档再代码）。
- 接口契约 / 鉴权 / 路由改动 → 停下问人（高风险闸口）。
- DI 注册后 → 写个空测试验证解析成功。
- Singleton 可写字段 → 必 `lock` 或 `ConcurrentDictionary`。
- 枚举/模式参数 → **int 收发**，约定写进类型注释 + BDD 步骤。
- 改 `src/` 提交 → 同笔暂存 `.quality-gate.json`（`cleared:true` + `codebaseOptimizer` 字段）+ commit 含 `Quality-Gate:` 行。
- 断言工作流终态 → 排除 Start/End 控制标记，只校验 Code(7)/Tool(6)。

### Don't（避开这些）
- 不混用 `EnsureCreated` / `MigrateAsync`（#31）。
- 不依赖测试宿主落盘文件做诊断（#38）；改 bug 后不用 `--no-build`（#39）。
- 不把设计文档当"冻结需求"——用户实战反馈优先，直接改文档+改实现（#34）。
- 不硬编码角色 code 与 DB 双源（曾致角色被误标"自定义"，#33）；统一以 DB 为准。
- 不在 `src/` 改动缺质量门 json（pre-commit 会拒，#45）。
- 不建命中 `.gitignore` 通配的目录名（曾误伤源码目录，#43）。

---

## 四、遗留与二期展望

### 一期已知缺陷（不是坑，是设计边界）
- Agent 是配置实体非运行时进程，`AgentCallStepExecutor` 忽略 `SystemPrompt`/`ModelEndpoint`（见 §8）。
- 记忆仅 RAG 检索，无 embedding 生成 / 语义·情节记忆 / 自动 compaction。
- 缺 durable/分布式执行层（同步编排器同请求跑完，长任务会阻塞 HTTP）。

### 第二期（F29–F33，🔴高风险，已解锁）
- 硬性阻塞（"第一期零 open"）已于 2026-08-11 满足，可以开工。
- 范围：Durable Execution / Agent 实体化 / 消息总线 / 语义记忆 / 在线评估门禁。
- 建议：🔴高风险，**先就 Durable Execution 方案与owner对齐再动手**，不要在未定方案时直接 feature-builder。

---

## 复盘自测（合上文档能答 = 真懂）

- 为什么 Phase 1 全 Stub？为什么 Phase 3 才做前端？Phase 5 为什么是 launch-blocking？
- EF 迁移铁律是什么？`TreatWarningsAsErrors` 下 EF 生成文件为何要 `#pragma warning disable IDE0161`？
- 客户端 Guid 主键为何要 `ValueGeneratedNever()`？不配会怎样？
- 模型枚举为何必须 int 收发？什么情况下会"协商模式选了却按顺序跑"？
- 诊断 BDD 测试为什么不能靠落盘文件？`--no-build` 的坑是什么？
- 工作流"假完成"的根因？控制标记断言为什么只校验 Code/Tool？
- 质量门 commit 被拒的常见原因？

---

## 参考
- `00-学习导读.md`（章节索引）· `06-common-pitfalls.md`（#1–#49 全坑 + 按症状查因表）
- `01-ddd` / `02-clean-arch` / `03-mediatr` / `04-ef` / `05-testing` / `07-evolution` / `08-decision` / `09-phase4` / `10-phase5` / `11-interview-qa`
- `features/backlog.md`（Tier 1 完成态）· `features/negotiation-productization.md`（F8 设计）
- 项目长期记忆 `MEMORY.md`（架构事实 / 内核现状 / 环境坑）
