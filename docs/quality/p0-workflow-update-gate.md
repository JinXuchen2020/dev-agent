# Quality Gate Report — P0 Workflow Update Endpoint

- **Phase**: `p0-workflow-update-endpoint`
- **Date**: 2026-07-22
- **Design**: `features/put-workflow-design.md`
- **Scope**: backend `PUT /api/v1/workflows/{id}` + `POST /{id}/run`, plus frontend edit-mode save (draft/run) and SSE-over-fetch with JWT.

## Reviewer (ddd-code-reviewer, adapted for .NET DDD + React/TS)
- Aggregate invariants preserved: `ReplaceSteps` re-indexes `Order` 0..n-1 and preserves same-name agent assignments; `Rename`/`UpdateContext` reused for non-step fields.
- Command/handler separation correct: `UpdateWorkflowCommand : ICommand<WorkflowDetailResponse?>` → `UnitOfWorkBehavior` persists the tracked aggregate (no orchestration primitive involved → no double-save). `RunExistingWorkflowCommand : IRequest` (NOT `ICommand`) because `IOrchestrationPrimitive.RunAsync` manages its own per-step persistence — mirrors the existing `RunWorkflowCommand` pattern.
- Tenant safety: handler returns `null` on `wf.TenantId != request.TenantId` → controller `404` (existence not disclosed).
- State guard: `Running`/`Paused` → `WorkflowConflictException` → `409` via new `IExceptionHandler` (`WorkflowConflictExceptionHandler`), registered in `Program.cs`.
- Frontend: edit mode now calls `updateWorkflow(id, …)` (PUT, carries id) instead of `runWorkflow` (POST, ignored id) — fixes the "save = duplicate" bug. SSE switched from `EventSource` to `fetch`+`ReadableStream` with `Authorization: Bearer` header; stream aborts on unmount and does not loop on non-2xx (fixes infinite reconnect + auth failure). `JSON.parse(wf.context)` wrapped in try/catch (no white-screen on empty/invalid context).

## Structure Gate (ddd-phase-quality-gate)
P0=0 P1=0 P2=0 P3=0. Handlers one-responsibility, aggregates own their invariants, no god-components.

## Codebase Optimizer (build + test + frontend QA)
- `dotnet build` (Api project, transitively Domain/Application/Infrastructure): 0 warnings, 0 errors.
- `dotnet test` Application.Tests: 65 passed. Api.Tests: 9 passed.
- Frontend `scripts/qa.mjs`: typecheck PASS, lint PASS, build PASS, unit PASS (OVERALL PASS).

## Closed defects
- B1 — edit mode now updates (PUT) instead of creating a duplicate.
- B2 — SSE carries JWT (fetch + Authorization header).
- B3 — SSE no longer infinite-reconnects (abort on unmount, non-2xx returns).
- B4 — `context` parse safe (try/catch fallback).
- backlog §5 P0 intent — implemented.

## Out of scope (still open, tracked in `features/backlog.md`)
- RAG R1–R4 (no ingest path / no tenant isolation / SQLite 500 / no threshold).
- Remaining O1–O14 (ErrorBoundary, 401 SPA, tenant/role hardcode, bundle split, test coverage, JWT XSS, dead code, 404 route, pagination, AbortController, a11y).
- DAG canvas (P1), node family, versioning, triggers, HITL (P2), publish/MCP/templates/trace (P3).
