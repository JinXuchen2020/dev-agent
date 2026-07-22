# 工作流更新端点设计（put-workflow-design）

> 状态：设计阶段（待实现）
> 优先级：P0（竞品路线图最高优先，直接修复「Workflow Editor 编辑态失效 / 保存即运行」）
> 关联：`features/backlog.md` B1（编辑态失效）/ B2-B4（SSE 鉴权·无限重连·context 白屏）、五节 P0 意图；`features/competitive-roadmap.md` §0 / §4 P0
> 性质：**接口契约 + 路由结构改动 → 高风险**。feature-dev 实施前会先停下确认本设计的契约要点（见 §7）。
> 生成日期：2026-07-22

---

## 0. 背景与问题

`WorkflowsController`（`src/AgentPlatform.Api/Controllers/WorkflowsController.cs`）当前只有：
- `GET /api/v1/workflows`（列表）
- `GET /api/v1/workflows/{id}`（详情）
- `POST /api/v1/workflows`（**创建并立即执行**，`RunWorkflowCommand` → `IOrchestrationPrimitive.RunAsync`）
- （`GET /api/v1/workflows/{id}/progress` SSE，见 B2）

**没有 `PUT/PATCH`**。工作流一旦创建即"创建并运行"，无法"编辑后保存"。前端 `/workflows/:id/edit` 调 `POST` 且忽略 `id`，本质是**后端不支持更新导致的必然行为**（非前端 bug）。

> 本设计只解决「P0 编辑/更新链路」。**DAG 画布 / 节点家族是 P1，不在本文范围**——P0 的 steps 仍是字符串名称列表（与现有模型一致），不引入 Node/Edge。

---

## 1. 目标 / 非目标

**目标**
- 新增 `PUT /api/v1/workflows/{id}`，支持**草稿更新（不执行）**：改名、更新 context、整体替换步骤名称列表。
- 修复 B1：前端编辑页调 PUT 带 id，"保存草稿"不触发执行。
- 提供"保存并运行 / 重新运行"的显式端点，避免"保存即新建重复工作流"。
- 维持现有租户隔离与 `Admin,Operator` 角色约束。

**非目标（留给 P1/P2）**
- 不引入 Node/Edge DAG 模型、不新增 StepType 枚举。
- 不做步骤级参数编辑（当前 steps 仅名称）、不做版本/草稿分支。
- 不做 SSE 鉴权改造本身（B2 的前端部分在本文 §5，后端 token 方案列为待拍板 §7）。

---

## 2. 后端端点设计

### 2.1 `PUT /api/v1/workflows/{id}` — 更新草稿（不执行）

```csharp
[Authorize(Roles = "Admin,Operator")]
[HttpPut("{id:guid}")]
public async Task<IActionResult> UpdateWorkflow(
    Guid id,
    [FromBody] UpdateWorkflowRequest request,
    CancellationToken ct = default)
{
    var command = new UpdateWorkflowCommand(
        id,
        request.Name,
        request.InitialContext,
        request.Steps,
        TenantId: _tenant.GetTenantId());
    var result = await _mediator.Send(command, ct);
    return result == null ? NotFound() : Ok(result);
}
```

**请求体（wire 格式 camelCase，与现有 `RunWorkflowRequest` 一致）**
```json
{
  "name": "修订后的工作流名",          // 可选；提供时改名
  "initialContext": "{ \"k\": \"v\" }", // 可选；提供时覆盖共享上下文
  "steps": ["步骤A", "步骤B", "步骤C"] // 可选；提供时【整体替换】步骤名称列表
}
```
- 三个字段**全部可选**，但至少提供一个，否则返回 `400 BadRequest("nothing to update")`。
- `steps` 为**整体替换**（与 `POST` 的 `Steps` 语义一致：仅名称字符串）；不保留被移除步骤的 `AgentAssignments` 中对应项（保留同名步骤的分配，见 §2.4）。

**响应**
- `200 OK` + `Workflow`（与 `GET {id}` 同形状，含 `steps` / `context` / `currentState` / `updatedAt`）。
- `400` 名称空 / 无字段提供 / `initialContext` 非空但非法 JSON。
- `404` 工作流不存在或**不属于当前租户**（不披露存在性）。
- `409 Conflict` 工作流处于 `Running` / `Paused` 态（不可编辑，需先结束）。

### 2.2 配套端点 `POST /api/v1/workflows/{id}/run` — 运行已有工作流

> 用于前端"保存并运行 / 重新运行"，**复用已有 id**，不再 `POST /workflows` 新建重复项（B1 的根因之一）。

```csharp
[Authorize(Roles = "Admin,Operator")]
[HttpPost("{id:guid}/run")]
public async Task<IActionResult> RunExistingWorkflow(
    Guid id,
    [FromBody] RunExistingWorkflowRequest? request,
    CancellationToken ct = default)
{
    var command = new RunExistingWorkflowCommand(
        id,
        request?.Preset ?? OrchestrationPreset.Sequential,
        TenantId: _tenant.GetTenantId());
    var result = await _mediator.Send(command, ct);
    return result == null ? NotFound() : Ok(result);
}
```
- 行为：按 id 加载（租户校验），调 `IOrchestrationPrimitive.RunAsync(existingWf, preset)` 重新执行；不新建聚合。
- 返回 `Workflow`。`404`（不存在/租户不符）、`409`（已在 Running）。

---

## 3. 聚合变更方法（新增于 `Workflow.cs`）

当前 `Workflow` 仅有 `AddStep` / `UpdateContext` / `AssignAgent`。需补：

```csharp
/// <summary>重命名工作流（仅草稿/非运行态调用）。</summary>
public void Rename(string name)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
    UpdatedAt = DateTime.UtcNow;
}

/// <summary>整体替换步骤名称列表；保留同名步骤的 Agent 分配。</summary>
public void ReplaceSteps(IReadOnlyList<string> stepNames)
{
    ArgumentNullException.ThrowIfNull(stepNames);
    var kept = new Dictionary<string, Guid>(_agentAssignments
        .Where(kv => stepNames.Contains(kv.Key)));
    _agentAssignments.Clear();
    foreach (var kv in kept) _agentAssignments[kv.Key] = kv.Value;

    _steps.Clear();
    for (var i = 0; i < stepNames.Count; i++)
        _steps.Add(new WorkflowStep(Guid.NewGuid(), i, stepNames[i]));
    UpdatedAt = DateTime.UtcNow;
}
```
> 注：`ReplaceSteps` 重置 `Order` 为 0..n-1（与 `RunWorkflowCommandHandler` 现有建步骤逻辑一致）。若需保留 step `Id` 稳定性（前端 key），后续 P1 DAG 再引入稳定 id 策略。

---

## 4. 命令与处理器

### 4.1 `UpdateWorkflowCommand`
```csharp
public record UpdateWorkflowCommand(
    Guid Id,
    string? Name,
    string? InitialContext,
    IReadOnlyList<string>? Steps,
    Guid TenantId
) : ICommand<Workflow?>;   // 注意：实现 ICommand → 走 UnitOfWorkBehavior 自动保存
```
> **为何这次实现 `ICommand<T>`**（与 `RunWorkflowCommand` 相反）：
> `RunWorkflowCommand` 不实现 `ICommand` 是因为编排原语内部已自己做 per-step 持久化，UoW 双写会冲突。而**更新路径无编排原语参与**，聚合经 `GetByIdAsync` 返回的是 EF 跟踪实体，由 `UnitOfWorkBehavior` 在管道末尾 `SaveChangesAsync` 提交即可——这是最干净的路径，也不会双写。

### 4.2 `UpdateWorkflowCommandHandler`
```csharp
internal sealed class UpdateWorkflowCommandHandler : IRequestHandler<UpdateWorkflowCommand, Workflow?>
{
    private readonly IWorkflowRepository _repo;
    private readonly ITenantProvider _tenant;

    public async Task<Workflow?> Handle(UpdateWorkflowCommand r, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(r.Id, ct);
        if (wf is null || wf.TenantId != r.TenantId) return null;   // 404，不披露存在性
        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new ConflictException("workflow is running/paused"); // → 409

        if (!string.IsNullOrWhiteSpace(r.Name)) wf.Rename(r.Name);
        if (!string.IsNullOrWhiteSpace(r.InitialContext)) wf.UpdateContext(r.InitialContext);
        if (r.Steps is { Count: > 0 }) wf.ReplaceSteps(r.Steps);

        _repo.Update(wf);   // 跟踪实体，UoWBehavior 实际提交；此调用显式标记 Modified
        return wf;
    }
}
```
- **租户安全**：`wf.TenantId != r.TenantId` → 返回 `null` → 控制器 `404`（与 `GetWorkflow` 一致）。不要返回 `403` 以免泄露存在性。
- **状态守卫**：`Running`/`Paused` → `409`。需新增一个轻量 `ConflictException`（或复用现有 `DomainException` + 控制器过滤为 409；确认项目现有异常→状态码映射）。
- `RunExistingWorkflowCommand` / Handler 类似：`GetByIdAsync` → 租户校验 → 状态守卫 → `await _primitive.RunAsync(wf, preset, ct)` → 返回 `wf`。

---

## 5. 前端配套修改（归属 P0，引用 backlog）

- `src/services/api.ts`：新增 `updateWorkflow(id, {name?, initialContext?, steps?})` → `PUT /workflows/{id}`；`runExistingWorkflow(id, preset?)` → `POST /workflows/{id}/run`。
- `src/pages/WorkflowEditorPage.tsx`：
  - `/edit` 路由保存改调 `updateWorkflow(id, ...)`（**带 id**）→ 修复 B1。
  - 拆「保存草稿」（仅 PUT，不执行）与「保存并运行」（PUT 成功后再 `runExistingWorkflow(id)`）。
  - 保存前校验至少 1 个 step（沿用 B11 修复）。
- `src/pages/WorkflowDetailPage.tsx`：
  - SSE 改用 `fetch` + `ReadableStream` 带 `Authorization` 头替代 `EventSource`（修复 B2）；`onerror` 中 `close()`（修复 B3）。
  - `JSON.parse(wf.context)` 包 `try/catch`（修复 B4，叠加 O1 ErrorBoundary）。

---

## 6. 验收清单（`.quality-gate.json` 须含）

- [ ] `dotnet build` 全绿（新增命令/处理器/异常）。
- [ ] `PUT /workflows/{id}` 单测：改名 / 改 context / 替换 steps 后持久化正确（二次 `GetByIdAsync` 验证）。
- [ ] 集成测试：跨租户 `PUT` 返回 `404`（不泄露存在性）。
- [ ] 集成测试：`Running`/`Paused` 态 `PUT` 返回 `409`。
- [ ] `POST /{id}/run` 单测：复用同一 id 重新执行，不新建重复工作流（db 计数不变）。
- [ ] 前端 QA（`scripts/qa.mjs` typecheck/lint/build/unit）：编辑页调 PUT、SSE 带 JWT、`context` 解析不白屏。
- [ ] 手动冒烟：编辑已有工作流 → 保存草稿 → 列表/详情反映修改；"保存并运行"复用同 id。

---

## 7. 待拍板决策（高风险，feature-dev 会停下问人）

1. **PUT 语义：部分更新 vs 全量更新**——本文采用部分更新（字段可选）。若你希望强制全量（PUT 语义严格），改为必填全部字段。
2. **`steps` 替换策略**——整体替换 vs 增量（增删改）。P0 采用整体替换（简单、与 POST 一致）；增量留 P1 DAG。
3. **`POST /{id}/run` 是否一并做**——本文建议做（否则"保存并运行"仍需 POST 新建重复项）。若想最小范围，可仅做 PUT，前端"保存并运行"暂用「PUT + 提示手动运行」。
4. **SSE 鉴权后端配合**——B2 前端改 `fetch`+token 后，后端是否额外支持 `?token=` 查询参数兜底（当无法用 Cookie 时）？涉及鉴权改动，需你拍板。
5. **`ConflictException` 落地方式**——新增异常类型 vs 复用现有领域异常 + 控制器映射；按项目现有异常约定。

---

## 8. 与路线图映射

- 直接闭合：`backlog` **B1**（编辑态失效）、**B3/B4**（SSE 重连/context 白屏属前端，但本设计提供 PUT 支撑编辑闭环）、**五节 P0 意图**。
- 上游依赖：无（不依赖 P1 DAG 模型）。
- 下游使能：P1 DAG 画布 MVP 可在本 PUT 端点上升级为 Node/Edge 感知（届时 `steps` 演进为节点集合）。

*遵循项目约定：设计先放 `features/`，实现再从 `backlog` 池顶取任务；接口契约/路由/鉴权层级改动由 feature-dev 先停下确认 §7 决策。*
