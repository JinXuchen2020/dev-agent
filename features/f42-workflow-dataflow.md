# F42 · 工作流数据流：节点显式输入映射 + 显式终端输出

> **优先级**：P1（补齐工作流引擎的数据流表达力，对标 Dify 变量引用模型）
> **风险**：中（涉及执行器契约、节点 schema、API 响应；向后兼容设计，不破坏存量工作流与 BDD 场景）
> **设计文档**：本文件即为设计文档，实现前需确认无异议
> **状态**：🟢 open（2026-08-29 立项）

---

## 1. 背景与动机

**现状（代码事实，2026-08-29 走查）**：

1. **数据不走边**。`SequentialOrchestrator.BuildWorkflowContext` 把所有已完成节点的 `Result` 按「节点名 → StepArtifact」拍平进 `WorkflowContext.Artifacts`，下游节点在 prompt 里看到全部上游产物（带 token 压缩摘要）。边（`WorkflowEdge`）目前**只承载控制流**——`ConditionStepExecutor` 读 outEdges 的 label 决定分支，不传数据。
2. **上下文污染 + 隐式耦合**：任何节点都能看到所有上游结果，长工作流 token 成本线性膨胀；节点改名会静默破坏下游对 `artifacts[name]` 的依赖，且无任何编译期/保存期校验。
3. **没有契约**：节点输出是裸字符串（`ContentType = "general"`），下游拿到什么全靠 prompt 碰运气。
4. **没有最终输出**：工作流跑完后「结果」= 最后一个完成节点的 result / `workflow.Context`，没有显式 Output 概念，触发方（API 调用/webhook）拿不到结构化返回值，只能翻 ExecutionLog。

**设计目标**：

1. **显式输入映射（Step A）**——节点在 `configJson` 中声明 `inputs`，用引用表达式从指定上游节点/触发载荷取值，注入 prompt 变量；未声明的旧工作流保持现有黑板行为（向后兼容）。
2. **显式终端输出（Step C）**——新增 `Output` 终端节点类型；工作流运行结果 = 所有已完成的 Output 节点集合，通过 API/SSE/trigger 回调暴露。
3. **保存期校验**——引用表达式指向的节点名/触发字段不存在时，保存工作流即报错（fail-fast），不留运行期静默空值。

**Non-goals（明确不做，避免与 F36 重叠）**：
- 类型化输出 schema + JSON Schema 校验（原 Step B，单独立题，且是 F34 字段级断言的前置）。
- Agent 级上下文隔离 / Blackboard 分区（F36 已立项）。
- 画布 React Flow port 模式连线选输出（本期仅做配置面板表单，port 模式作为后续前端迭代）。

---

## 2. 变更范围

| 文件/模块 | 变更类型 | 说明 |
|-----------|----------|------|
| `src/AgentPlatform.Domain/Aggregates/Workflows/` | 修改 | 节点 `configJson` 契约增加 `inputs` 段；新增 `StepType.Output` 枚举值；保存时校验引用合法性（`EnsureGraphSynced` 附近） |
| `src/AgentPlatform.Application/Abstractions/` | 新增 | `IInputResolver`（引用表达式解析契约）+ `InputReference` 值对象 |
| `src/AgentPlatform.Infrastructure/Workflows/InputResolver.cs` | 新增 | 解析 `{{nodes.<name>.output}}` / `{{trigger.<path>}}` / `{{blackboard.<key>}}`，产出 `Dictionary<string,string>` prompt 变量 |
| `src/AgentPlatform.Infrastructure/Workflows/SequentialOrchestrator.cs` | 修改 | `BuildWorkflowContext` 增加 `ResolvedInputs`；节点声明了 `inputs` 时，执行器只注入解析后的变量（不再拍平全量 artifacts） |
| `src/AgentPlatform.Infrastructure/Workflows/AgentCallStepExecutor.cs` 等 | 修改 | 各执行器优先消费 `ctx.ResolvedInputs` 渲染 prompt；未声明时回退现有 artifacts 行为 |
| `src/AgentPlatform.Infrastructure/Workflows/OutputStepExecutor.cs` | 新增 | Output 节点执行器：聚合上游解析结果（或透传声明引用），写入节点 Result |
| `src/AgentPlatform.Infrastructure/Persistence/Configurations/WorkflowConfiguration.cs` | 修改 | 若新增终端集合/字段则同步 EF 映射（优先复用现有 Nodes owned collection，不新增表） |
| `src/AgentPlatform.Api/`（工作流运行/触发端点） | 修改 | 运行完成响应增加 `outputs: { <outputNodeName>: <result> }`；webhook trigger 同步场景直接返回该结构 |
| `src/AgentPlatform.Web/` | 修改 | 节点配置抽屉增加「输入映射」编辑器（key + 引用表达式，带上游节点名下拉）；节点面板识别 Output 类型 |
| `src/AgentPlatform.SpecFlowTests/` | 新增 | 数据流 BDD 场景（见 §4） |
| `docs/AGENT_PLATFORM_BLUEPRINT.md` | 修改 | 同步工作流数据流章节 |

---

## 3. 详细设计

### 3.1 引用表达式语法（InputResolver）

```
{{nodes.<节点名>.output}}      // 指定上游节点的 Result（整个字符串）
{{nodes.<节点名>.output.json.<字段路径>}}   // 若 Result 是合法 JSON，取字段路径值（如 .output.json.summary）
{{trigger.<字段路径>}}          // 触发载荷（webhook body / API 请求体 / 定时器元数据）
{{blackboard.<key>}}           // F30 Blackboard 已有键（复用，不新增存储）
```

解析规则（fail-fast）：
- 节点名不存在 / 节点未完成 / JSON 字段路径取不到 → **保存工作流时报错**（静态校验节点存在性），**运行时报错**（未完成、JSON 解析失败），不静默回空串。
- 表达式整体可嵌在模板字符串中：`"请基于 {{nodes.researcher.output.json.summary}} 撰写"`，做字符串插值；`inputs` 值为纯引用时传完整值不做插值。

### 3.2 configJson 契约

```jsonc
// AgentCall 节点 configJson 示例（向后兼容：无 inputs 段 = 旧行为）
{
  "prompt": "基于研究摘要撰写文章",
  "inputs": {
    "summary": "{{nodes.researcher.output.json.summary}}",
    "topic": "{{trigger.body.topic}}"
  }
}
```

- `ResolvedInputs` 注入 prompt 变量：执行器渲染 `{{summary}}` 等占位符到 system/user prompt；变量集随 ExecutionLog 落库（脱敏后），可回放。

### 3.3 执行语义（SequentialOrchestrator）

- 节点声明了 `inputs` → `WorkflowContext.ResolvedInputs` 非空，执行器**只**使用解析后的变量构建 prompt（数据流模式）。
- 未声明 `inputs` → `ResolvedInputs` 为空，回退现有 artifacts 拍平行为（黑板模式）。
- 两种模式可在同一工作流共存（逐节点决定），保证灰度迁移。

### 3.4 Output 终端节点

- 新增 `StepType.Output`：单入单出，`configJson` 可选声明 `inputs`（不声明则聚合全部已完成 Output 可达节点的结果——简化为：透传其唯一上游节点的 Result）。
- 运行完成时，工作流聚合所有 `StepType.Output` 且 `State == Completed` 的节点：
  ```jsonc
  // API 运行完成响应新增字段
  { "workflowId": "...", "state": "Completed", "outputs": { "finalArticle": "<result>" } }
  ```
- 无 Output 节点的存量工作流：`outputs` 为空对象，行为不变。
- webhook trigger 同步等待场景：完成响应直接携带 `outputs`；异步场景通过现有事件/SSE 通道下发。

### 3.5 保存期校验

`Workflow` 聚合新增 `EnsureInputsValid()`（保存命令管道调用）：
1. 每个 `inputs` 引用的 `nodes.<name>` 必须存在于本工作流；
2. 不允许自引用/环引用（沿边做 DFS 检测）；
3. `trigger.*` 首段路径与 trigger 类型白名单匹配（`body`/`query`/`headers`/`schedule`）。
校验失败抛 `WorkflowGraphException` 子类 `InvalidInputReferenceException`，前端保存表单直接显示。

---

## 4. 验收标准（feature-builder 消费）

| # | 验收子项 | 优先级 |
|---|----------|--------|
| A1 | `InputResolver` 单元测试覆盖三类引用 + JSON 字段路径 + 全部 fail-fast 分支 | P0 |
| A2 | 节点声明 `inputs` 后，下游 prompt 只含解析变量（可从 ExecutionLog 断言），不再拍平全量 artifacts | P0 |
| A3 | 未声明 `inputs` 的存量工作流行为与现状逐字节一致（现有 114 后端 BDD + 27 前端 E2E 全绿） | P0 |
| A4 | `Output` 节点执行 + API `outputs` 字段 + webhook 同步返回，BDD 场景覆盖 | P0 |
| A5 | 保存期校验：引用不存在/环引用/trigger 路径非法 → 保存失败并返回可读错误 | P1 |
| A6 | 前端节点配置抽屉「输入映射」编辑器 + Output 节点类型渲染 + playwrright-bdd 场景（画布配置 inputs → 运行 → 断言 outputs 展示） | P1 |
| A7 | 文档同步（BLUEPRINT 工作流章节 + README 特性说明） | P2 |

---

## 5. 实施切分建议

1. **第一刀（后端数据流）**：A1–A3，纯后端，Domain + Infrastructure，先行合入。
2. **第二刀（终端输出）**：A4–A5，含 API 契约变更（新增字段，非破坏性）。
3. **第三刀（前端）**：A6，配置面板 + E2E。
4. **收尾**：A7 文档。

---

## 6. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 双模式共存导致执行器分支复杂度上升 | `ResolvedInputs` 为唯一判据，执行器入口统一「有则用、无则回退」，不散落 if |
| JSON 字段路径对大 Result 的性能 | 解析带大小上限（复用 16k configJson 上限思路），超限报错而非截断 |
| 节点改名破坏引用 | 保存期校验兜底 + 后续迭代可在 rename 命令中做引用重写（本期不做） |
