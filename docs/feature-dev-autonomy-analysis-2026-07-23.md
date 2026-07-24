# feature-dev 自主流水线可行性分析

> 分析日期：2026-07-23 ｜ 问题：能否让 feature-dev 从 `features/backlog.md` 自动取任务 → 自主完成 + 验证 + commit + push，减少人工干预？
> 结论先行：**全池"一键自治"不可行；安全前端子集可行；高价值后端行动层（A1/A2/Research）不应全自治。commit/push 在当前环境有三道硬闸门，且 push 已经实测失败。**

---

## 1. feature-dev 实际是什么（能力边界）

读 `.workbuddy/skills/feature-dev/SKILL.md` 后得到硬约束：

- **定位**：标题即「**前端**功能自主迭代流程（AgentPlatform.Web，React19+TS+Vite+AntD）」。它**只做前端**，不碰后端。
- **取任务**：读 backlog 取最靠前 `open` → 标 `doing` → 实现 → 跑 `node scripts/qa.mjs --e2e` → 全绿标 `done`。
- **验证手段**：仅前端 `qa.mjs`（typecheck/lint/build/unit/+e2e）。**无后端构建/测试/质量 skill 环节**。
- **护栏（不可越界）**：
  - ❌ **"不执行 git commit / push / 建 PR，除非用户明确要求"** —— 当前 skill 设计上**根本不做 commit/push**。
  - ⚠️ **"高风险停下问人"**：涉及**接口契约、鉴权/角色、路由结构、破坏性后端改动**时，停止自动修改，汇报选项等用户决定。
  - ❌ "绝不自己发明功能需求"（只做 backlog / 用户指令里有的）。

> 所以"自主 commit + push"对现在的 feature-dev 而言，不仅是能力缺失，而是**被护栏明确禁止**的。

---

## 2. backlog 当前 open 任务全量 + 自治分类

（以下 open 项来自 `features/backlog.md` 实测枚举；done 项已剔除）

| ID | 内容 | 端 | 自治风险 | 分类 |
|----|------|----|---------|------|
| **B6** | 登录密码形同虚设（前端不校验/不发送） | 前端 | 踩 auth 语义 | C（须问人）|
| **B7** | Dashboard 硬编码假数据 | 前端 | 若接真实接口需确认来源 | B/C（视是否新增端点）|
| **B8** | ApiKeys 页全 Mock + 死按钮 | 前端 | **阻塞：后端无 ApiKey REST 端点** | B（后端先就位）|
| **B9** | AgentConfigurations YAML 从不展示 | 前端 | 纯展示，低风险 | **A（可直接自治）** |
| **B10** | 状态筛选枚举大小写可能不匹配 | 前端 | 仅核对后端枚举，不改契约 | **A** |
| **B11** | Workflows 快速运行无错误处理/可能建空 | 前端 | 低风险 | **A** |
| **A1** | 工具调用执行层全空心（三 `IToolExecutor` 桩） | **后端** | 高风险"名不副实现"区，须 `ddd-code-reviewer` | **D（超范围）** |
| **A2** | 代码沙箱为桩（DockerCodeSandbox） | **后端** | 同上，Phase 6 验收 1 | **D** |
| **O1** | 全站缺 ErrorBoundary | 前端 | 已借 ErrorBoundary 新增部分落地但未标 done | **A**（核实后标 done）|
| **O2** | 401 整页跳转破坏 SPA | 前端 | 改鉴权流转逻辑 | C（须问人）|
| **O3** | 鉴权态不一致（demo 登录路径） | 前端 | 踩 auth 态 | C |
| **O4** | 用户/租户信息硬编码、搜索/切换是装饰 | 前端 | 低风险（填真实值/禁装饰） | A/B |
| **O5** | API 错误被静默吞没无错误态 | 前端 | 低风险 | **A** |
| **O6** | 打包体积过大未拆包 | 前端 | 低风险（懒加载/manualChunks） | **A** |
| **O7** | 单测覆盖极低 | 前端 | 低风险（补测试） | **A** |
| **O8** | JWT 存 localStorage 有 XSS 风险 | 前端 | **安全相关**，改存储策略 | C（须问人）|
| **O9** | antd 静态 message 告警 | 前端 | 低风险 | **A** |
| **O10** | 死代码/未用能力 | 前端 | 低风险 | **A** |
| **O11** | 无 404 兜底路由/文档链接失效 | 前端 | 低风险 | **A** |
| **O12** | 列表分页未接 totalCount | 前端 | 低风险 | **A** |
| **O13** | 无请求取消/卸载 setState 风险 | 前端 | 低风险 | **A** |
| **O14** | 可访问性薄弱 | 前端 | 低风险 | **A** |
| **四·ApiKeys真实化** | 后端端点就绪后接 CRUD | 前端+**后端** | 阻塞于后端端点 | B |
| **四·Conversations 搜索/筛选** | 复用现有数据加搜索筛选 | 前端 | 低风险 | **A** |
| **四·Research Agent** | SerpAPI 多步调研 | **后端+外部API** | 高风险叙事模块，须 `ddd-code-reviewer` | **D** |
| **五·版本管理** | Workflow 版本快照/回滚/导入导出 | **后端** | 高风险（聚合变更） | D |
| **五·节点全家桶** | Code/HTTP/Tool/Condition/Loop/HITL… | 后端+**前端画布重构** | high-risk（接口/画布） | D |
| **五·触发器** | Webhook/cron/Chat 触发 | **后端** | high-risk | D |
| **五·发布为 API/MCP** | 每工作流生成 REST/MCP | **后端** | high-risk | D |
| **五·模板市场** | 内置行业模板克隆 | 前端+后端 | 中 | D/B |
| **五·Trace/评估视图** | 节点级 trace + 回归评估 | 前端+**后端** | 中 | D/B |
| **五·工作流调试器** | 变量监视+单步重跑 | 前端+**后端** | 中 | D/B |
| **五·企业增强** | 多工作空间/用量仪表盘/diff | **后端** | 中 | D |

**分类说明**
- **A 可直接自治**：纯前端、低风险、后端依赖已满足。约 14 项（B9/B10/B11/O1/O5/O6/O7/O9/O10/O11/O12/O13/O14 + Conversations 搜索）。
- **B 需后端先就位（阻塞）**：B8、ApiKeys 真实化（后端无 ApiKey 端点）、B7（若需新增统计端点）。
- **C 前端但踩红线（须问人）**：B6、O2、O3、O8（auth/安全语义变更），及 B7 若涉及新接口。
- **D 超出 feature-dev 范围（后端/全栈）**：A1、A2、Research Agent、五节大部分（版本管理/节点全家桶/触发器/发布MCP/企业增强）及 Trace/调试器/模板市场的后端部分。**这些恰恰是项目最高价值、最该做的"行动层"任务**，但 feature-dev 一个都做不了，且按护栏本就不该让代理在无人审批下改接口/安全/聚合。

---

## 3. 三个环节的落地可行性

### 3.1 取任务 + 完成
- feature-dev 能"取 + 做"的只有 **A 类（约 14 项安全前端任务）**。
- **B/C/D 类它要么做不了（后端），要么必须停下问人（红线）**。A1/A2/Research 这些最关键的"让平台真正能用"的任务，全在 D 类。

### 3.2 验证
- 前端：`node scripts/qa.mjs --e2e` **可自动跑**（我们已实测 4 闸门全绿），能作为 A 类任务的自动回归证据。
- 后端：feature-dev **不做** `dotnet build`/`dotnet test`，也**不跑** `ddd-code-reviewer`/`ddd-phase-quality-gate`/`codebase-optimizer`。而 D 类任务正是这些质量 skill 的强制范围。

### 3.3 commit + push（三道硬闸门）
1. **skill 护栏禁止**：feature-dev 明文"不执行 commit/push，除非用户明确要求"。→ 即便想自治，也得先改 skill。
2. **pre-commit 质量门**（已实测读取 `scripts/git-hooks/pre-commit`）：凡改 `src/` 的提交，必须**连同 `.quality-gate.json`（cleared:true + codebaseOptimizer 字段）一起暂存**，且 commit-msg 含 `Quality-Gate:` 行。这意味着**一次合法提交 = 跑完 3 个（部分为交互式）质量 skill + 写标记文件 + 写 message**。这是重活，不是"自动"。
3. **push 实测已失败**（2026-07-23 记录）：`github.com:443` 两次 Connection reset/timeout，非交互 shell 无凭据提示 `could not read Username`。→ **在本环境 push 当前就是坏的那一环**，不论 skill 怎么写。

> 另外 CI（`.github/workflows/ci.yml` 的 quality-gate job）在 push/PR 含 `src/` 改动时也会校验质量门——即使本地强推绕过，远端仍卡。

---

## 4. 结论

| 诉求 | 是否可行 | 说明 |
|------|---------|------|
| feature-dev 自动取任务 | ✅ 部分 | 仅 A 类（安全前端）；B/C/D 取了也做不了或须问人 |
| 自主完成 | ✅ 部分 | A 类可；D 类（后端/全栈）超范围；C 类须问人 |
| 自动验证 | ✅ 前端 | qa.mjs 可自动；后端验证缺失 |
| 自主 commit | ⚠️ 需改 skill + 跑质量门 | 当前禁止；且质量门要求 3 skill 全绿 |
| 自主 push | ❌ 当前不可行 | 环境网络/凭据已失败；CI 也卡质量门 |

**一句话**：把"减少人工干预"理解为"让代理自动把 backlog 里的安全前端 bug 修掉并跑绿 QA"——**可行且值得做**；但理解为"端到端无人值守地把 A1/A2/Research 这类核心后端能力补实并推上主干"——**不可行、也不应该**（设计决策 + 安全/接口红线必须由人拍板）。

---

## 5. 落地"减少人工干预"的推荐路径

1. **分 Tier 自治，别追求全自治**
   - **Tier A（全自动）**：A 类安全前端任务 → 扩展 feature-dev 或新建 `frontend-dev` 流水线，含"取任务→实现→qa.mjs 全绿→标 done"。
   - **Tier D（半自动 + 人审）**：A1/A2/Research/节点全家桶等 → 新建 `backend-dev` 流水线，自动化"实现 + `dotnet build/test` + `ddd-code-reviewer`/`ddd-phase-quality-gate` + 写 `.quality-gate.json`"，但**只 commit 到特性分支、开 PR，合入留人审**；涉及接口/安全/聚合变更时**停下来给选项**。

2. **轻量化前端改动的提交闸门**：A 类是纯 UI/展示修复，让 `pre-commit` 对"仅改 `src/AgentPlatform.Web/` 且不含 `src/AgentPlatform.*/`"的提交豁免完整质量门（以 qa.mjs 全绿为等价证据），否则每个 Dashboard 文案修复都要跑三道交互式质量 skill，成本失衡。

3. **修 push 这道硬阻塞**：配置 GitHub token / 换可达远端，或把"push"降级为"push 到特性分支"，主干合入保持人工。在 push 修好前，任何"自主 push"都是空谈。

4. **保留 human-gate 在关键路径**：接口契约、RBAC/角色、路由结构、安全存储（O8）、破坏性后端改动——这些是平台的安全与正确性命门，**人工审批不是负担，是护栏**。自治应消除"机械验证 + 任务分诊"的人力，而非消除"关键决策"的人力。

---

## 附：当前环境实测佐证
- 后端：`dotnet build` 0/0；`dotnet test` 204/0 绿。
- 前端：`npm run build` 通过；`node scripts/qa.mjs` 4 闸门全绿（历史实测）。
- push：`github.com` 不可达（Connection reset/timeout），非交互凭据缺失——2026-07-23 两次尝试均失败，改动留本地分支。
