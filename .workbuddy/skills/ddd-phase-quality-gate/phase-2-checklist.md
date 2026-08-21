# Phase 2 Checklist: Multi-Agent Workflow

Phase 2 specific items supplementing the general checklist. Merge these into the generated `phases/phase-2-checklist.md`.

---

## Phase 2 Scope

- AutoGen.NET multi-agent collaboration (6 roles: Requirement -> Product -> Architecture -> Dev -> Test -> Docs)
- AgentRole enum -> AgentType value object migration
- Self-built state machine engine (branching, retry, rollback)
- Redis short-term memory
- EF Core persistence with migrations
- ExecutionLog table + MediatR event-driven logging

---

## 1. Pre-flight Version Audit (Phase 2 Specific)

- [ ] AutoGen.NET version locked and recorded in blueprint
- [ ] AutoGen.NET compatible with Semantic Kernel 1.30 (verify no version conflict)
- [ ] StackExchange.Redis version locked and recorded
- [ ] StackExchange.Redis compatible with .NET 9
- [ ] AutoGen.NET core API verified: `IAgent`, group chat, termination condition signatures
- [ ] StackExchange.Redis `IDatabase` API verified: `StringSetAsync`, `StringGetAsync`, `KeyExpire`
- [ ] EF Core 9.0.4 `dotnet ef` tooling available and working
- [ ] Minimum console verification for AutoGen.NET API surface
- [ ] Minimum console verification for StackExchange.Redis API surface

## 2. BDD Scenarios (Phase 2 Specific)

- [ ] `AgentTypeMigration.feature` — old AgentRole agents migrate to AgentType
- [ ] `WorkflowStateMachine.feature` — normal flow, branch, retry x3, rollback after exhausted retries
- [ ] `MultiAgentPipeline.feature` — input requirement -> 6 agents -> architecture+code+test+docs output
- [ ] `ExecutionLog.feature` — queryable history with status, duration, error details
- [ ] `CustomAgentRole.feature` — user creates custom agent role type
- [ ] `RedisMemoryExpiry.feature` — short-term memory expires and degrades gracefully

**State machine edge cases in .feature:**
- [ ] Step succeeds -> transition to next state
- [ ] Step fails -> retry (up to 3)
- [ ] Retry exhausted -> rollback
- [ ] Branch condition met -> alternate path
- [ ] Branch condition not met -> default path
- [ ] Concurrent execution of same workflow
- [ ] State recovery after Redis memory expiry
- [ ] State persistence to EF Core + recovery after restart

## 3. DDD Layer Rules (Phase 2 Specific)

New interfaces this phase:

- [ ] `IAgentOrchestrator` — Application.Abstractions — impl: `AutoGenAgentOrchestrator` in Infrastructure
- [ ] `IStateMachineEngine` — Application.Abstractions — impl: `WorkflowStateMachineEngine` in Infrastructure
- [ ] `IExecutionLogRepository` — Domain.Repositories — impl: `ExecutionLogRepository` in Infrastructure
- [ ] `IExecutionLogWriter` — Application.Abstractions — impl: `ExecutionLogWriter` in Infrastructure (if separate from repository)
- [ ] Any new domain event interfaces follow `IDomainEvent` pure interface pattern
- [ ] Domain project .csproj still has zero external NuGet dependencies (AutoGen.NET must NOT be referenced by Domain)

## 4. DI Registration (Phase 2 Specific)

- [ ] `IAgentOrchestrator` -> `AutoGenAgentOrchestrator` — lifetime: Scoped or Singleton (determine based on state)
- [ ] `IStateMachineEngine` -> `WorkflowStateMachineEngine` — lifetime: Scoped (per workflow execution)
- [ ] `IExecutionLogRepository` -> `ExecutionLogRepository` — lifetime: Scoped
- [ ] `IConnectionMultiplexer` -> Redis connection — lifetime: Singleton
- [ ] `IShortTermMemory` implementation switches from `InMemoryShortTermMemory` to `RedisShortTermMemory` (conditional or environment-based)
- [ ] AutoGen.NET `Agent` instances: factory registration (not direct DI, as agents may need runtime configuration)
- [ ] `StubWorkflowEngine` replaced with real `WorkflowStateMachineEngine` (remove stub registration)
- [ ] MediatR domain event handlers for ExecutionLog registered (INotificationHandler<WorkflowStepCompleted> etc.)

## 5. Configuration-First (Phase 2 Specific)

- [ ] `AutoGenSettings` — agent model assignments, max conversation rounds, termination condition params
- [ ] `RedisSettings` — connection string, default expiry seconds, key prefix
- [ ] `StateMachineSettings` — max retry count (default 3), rollback timeout, step timeout
- [ ] `ExecutionLogSettings` — retention days, batch write threshold, SSE enabled
- [ ] All above registered in `appsettings.json` AND `appsettings.QuickStart.json`
- [ ] No hardcoded retry counts, timeouts, or Redis keys in business code
- [ ] `IConnectionMultiplexer` connection string from configuration, not hardcoded

## 6. EF Core Mapping (Phase 2 Specific)

- [ ] `AgentType` value object: `OwnsOne` mapping on `Agent` aggregate (replaces `AgentRole` column)
- [ ] Migration to rename/convert `AgentRole` column to `AgentType` (handle existing data)
- [ ] `ExecutionLog` aggregate root: table mapping with all fields
- [ ] `ExecutionLogEntry` value object or entity: `OwnsMany` or separate table
- [ ] `WorkflowStep` state field: enum to string conversion or value converter
- [ ] `WorkflowTransition` (if exists): `OwnsMany` on `Workflow` aggregate
- [ ] `dotnet ef migrations add Phase2MultiAgent` succeeds
- [ ] Migration script reviewed: does not DROP existing tables or columns destructively
- [ ] `dotnet ef database update` succeeds on clean database
- [ ] `dotnet ef database update` succeeds on Phase 1 database (forward migration)

## 7. Concurrency and Lifecycle (Phase 2 Specific)

- [ ] State machine: concurrent workflow executions don't corrupt shared state
- [ ] State machine: if Singleton, all mutable state protected with `lock` or `ConcurrentDictionary`
- [ ] State machine: if Scoped, verify no cross-request state leakage
- [ ] ExecutionLog: concurrent Agent writes are safe (consider `Channel<T>` for async queue)
- [ ] Redis short-term memory: `ConnectionMultiplexer` is Singleton (not per-request)
- [ ] Redis operations handle connection failures (try-catch with fallback to InMemory)
- [ ] AutoGen group chat: message ordering in concurrent scenarios
- [ ] `CostController` (Phase 1 carryover): verify still thread-safe with multi-agent concurrent calls

## 8. Cross-Cutting Infrastructure (Phase 2 Specific)

- [ ] Workflow start/query endpoints: through MediatR commands/queries
- [ ] ExecutionLog query endpoint: through MediatR query
- [ ] New commands marked `ICommand<T>` (trigger SaveChanges)
- [ ] Domain events for workflow lifecycle: `WorkflowStarted`, `StepCompleted`, `StepFailed`, `WorkflowCompleted`, `WorkflowRolledBack`
- [ ] Event handler ordering: domain events flushed BEFORE `SaveChangesAsync` (UnitOfWorkBehavior pattern)
- [ ] ExecutionLog written via domain event handler (not direct call in state machine)
- [ ] SSE streaming endpoint for workflow progress (if acceptance criterion requires)
- [ ] Health Check includes Redis connectivity check
- [ ] `UseAuthorization` enabled if JWT implemented this phase (Phase 1 left it commented out)
- [ ] Multi-tenant query filter: if dynamic tenant switching implemented, verify `ITenantProvider` updated

---

## Incremental Gate Sequence (Phase 2)

```
Module 1: AgentType value object migration
  - [ ] AgentType record defined in Domain
  - [ ] Agent aggregate updated
  - [ ] IAgentRepository interface updated (roleCode parameter)
  - [ ] AgentRepository updated
  - [ ] CreateAgentCommandHandler updated
  - [ ] EF Core mapping updated
  - [ ] Migration created and verified
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green
  - [ ] SpecFlow AgentTypeMigration.feature green

Module 2: State machine engine
  - [ ] State definitions and transitions
  - [ ] Branching logic
  - [ ] Retry logic (max 3)
  - [ ] Rollback logic
  - [ ] Unit tests for all edge cases
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green
  - [ ] SpecFlow WorkflowStateMachine.feature green

Module 3: Redis short-term memory
  - [ ] RedisShortTermMemory implements IShortTermMemory
  - [ ] IConnectionMultiplexer registered as Singleton
  - [ ] Expiry logic implemented
  - [ ] Connection failure fallback
  - [ ] Unit tests with mock IConnectionMultiplexer
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green

Module 4: AutoGen multi-agent collaboration
  - [ ] 6 agent roles defined
  - [ ] AutoGenAgentOrchestrator implemented
  - [ ] Group chat management and termination conditions
  - [ ] DI registration (factory pattern for agents)
  - [ ] Unit tests with mock IChatCompletionService
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green

Module 5: ExecutionLog
  - [ ] ExecutionLog aggregate root
  - [ ] IExecutionLogRepository + implementation
  - [ ] Domain event handlers for workflow lifecycle events
  - [ ] EF Core mapping
  - [ ] Migration created
  - [ ] Query endpoint
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green
  - [ ] SpecFlow ExecutionLog.feature green

Module 6: End-to-end integration
  - [ ] Full pipeline: requirement -> 6 agents -> output
  - [ ] State machine persistence + recovery
  - [ ] ExecutionLog captures all steps
  - [ ] SpecFlow MultiAgentPipeline.feature green
  - [ ] SpecFlow CustomAgentRole.feature green
```

---

## Phase 2 High-Risk Predictions

Based on Phase 1 patterns, these are most likely to require multi-round fixes:

1. **AutoGen.NET API mismatch** — group chat, termination condition API may differ from expectations. Pre-flight version audit (Module 0) prevents this.
2. **State machine edge cases** — retry/rollback/concurrent/recovery. BDD-first with edge case scenarios prevents this.
3. **AgentType migration cascade** — enum to value object touches all layers. Incremental gate with compile-test after each file prevents this.
4. **ExecutionLog event ordering** — domain events vs SaveChanges order. UnitOfWorkBehavior pattern from Phase 1 provides the template.
5. **Redis connection lifecycle** — Singleton vs Scoped, connection failure handling. Concurrency audit catches this.
