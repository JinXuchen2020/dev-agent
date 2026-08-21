# General Phase Checklist Template

Universal checklist for any .NET DDD project phase. Use this as a template and write the result into the `## Quality Gate Checklist` section of the referenced phase document — **never create a separate `phases/phase-N-checklist.md` file**.

---

## 1. Pre-flight Version Audit

Complete BEFORE writing any code.

- [ ] All new NuGet packages installed and version numbers recorded
- [ ] API signatures verified against actual installed versions (not training data)
- [ ] Blueprint updated with locked version numbers
- [ ] Known API differences from previous phases documented in learning notes
- [ ] `dotnet build` passes with existing code before any new code is added

**Phase-specific packages to verify:**
- [ ] (fill in package names and versions)

**API differences to check:**
- [ ] (fill in API surface areas to verify)

---

## 2. BDD Scenarios First

Write SpecFlow `.feature` files BEFORE implementing each feature.

- [ ] Feature file 1: (name) — scenarios: (list)
- [ ] Feature file 2: (name) — scenarios: (list)
- [ ] All scenarios initially RED (failing because not implemented)
- [ ] Each scenario covers at least one acceptance criterion
- [ ] Edge case scenarios written (failure, retry, boundary, empty, concurrent)

**Edge case checklist for each scenario:**
- [ ] Happy path
- [ ] Failure path (exception, timeout, invalid input)
- [ ] Boundary condition (max retries, empty collection, null value)
- [ ] Concurrent access (if applicable)

---

## 3. DDD Layer Rules

Three iron rules. Check EVERY new interface and implementation class.

- [ ] Interface defined in `Application.Abstractions` or `Domain.Abstractions`
- [ ] Implementation class in `Infrastructure` corresponding subdirectory
- [ ] DI registration in `Infrastructure/DependencyInjection.cs`
- [ ] Domain project has zero external NuGet dependencies (check .csproj)
- [ ] Application project does not reference Infrastructure project
- [ ] Api layer only calls `AddApplication()` and `AddInfrastructure()`, no direct service registration

**New interfaces this phase:**
- [ ] (interface name) — defined in: ___ — implemented in: ___ — registered: ___
- [ ] (interface name) — defined in: ___ — implemented in: ___ — registered: ___

---

## 4. DI Registration Completeness

Every new interface MUST have its implementation registered in the same increment.

- [ ] All new interfaces have implementation classes
- [ ] All implementation classes are registered in `Infrastructure/DependencyInjection.cs`
- [ ] DI lifetime is correct (Scoped for request-scoped, Singleton for shared state, Transient for stateless)
- [ ] Decorator pattern: if used, factory registration method documented
- [ ] Conditional registration: if used, condition documented
- [ ] `IServiceProvider` can resolve all new services (verify with a test or manual check)

**Registration checklist:**
- [ ] (interface) -> (implementation) — lifetime: ___
- [ ] (interface) -> (implementation) — lifetime: ___

---

## 5. Configuration-First

All variable values go through `IOptions<T>`. No hardcoded magic numbers or strings.

- [ ] All new configuration classes defined (e.g. `XxxSettings`)
- [ ] Configuration classes registered with `services.Configure<XxxSettings>(section)`
- [ ] `appsettings.json` has the configuration section
- [ ] `appsettings.QuickStart.json` has the configuration section (if QuickStart profile used)
- [ ] No hardcoded GUIDs, URLs, model names, pricing values, retry counts, or budgets
- [ ] All values injected via `IOptions<T>` or `IOptionsSnapshot<T>`

**New configuration classes this phase:**
- [ ] (class name) — settings: (list fields)
- [ ] (class name) — settings: (list fields)

---

## 6. EF Core Mapping Sync

Every aggregate root, entity, and value object has an EF Core configuration.

- [ ] `IEntityTypeConfiguration<T>` class created for each new aggregate root
- [ ] Value objects mapped with `OwnsOne` / `OwnsMany`
- [ ] Collections use `UsePropertyAccessMode(Field)` if exposed as `IReadOnlyList<T>`
- [ ] Shadow properties have `.ValueGeneratedOnAdd()`
- [ ] Column name conflicts resolved with `.HasColumnName()`
- [ ] `dotnet ef migrations add` succeeds without errors
- [ ] `dotnet ef database update` succeeds
- [ ] Migration does not break existing tables (verify migration script)

**New/modified mappings this phase:**
- [ ] (aggregate/VO name) — configuration class: ___
- [ ] (aggregate/VO name) — configuration class: ___

---

## 7. Concurrency and Lifecycle

Review all mutable shared state.

- [ ] All Singleton services with mutable state use `lock` or `ConcurrentXxx`
- [ ] All Scoped services do not hold cross-request mutable state
- [ ] `decimal` accumulators use `lock` (not `Interlocked`)
- [ ] Collections use `ConcurrentDictionary` / `ConcurrentQueue` etc. if accessed concurrently
- [ ] Daily reset logic tested (if applicable)
- [ ] Redis operations handle connection failures gracefully
- [ ] State machine handles concurrent workflow execution (if applicable)
- [ ] **Behavioral: `Pause`/`Cancel` state changes are observed by the execution loop at each step boundary** (not only `CancellationToken`) — mid-step interruption is possible if the blueprint requires it
- [ ] **Behavioral: retry/loop boundary uses `<` not `<=` — actual attempt count equals configured `maxRetries` (no off-by-one)**
- [ ] **Behavioral: placeholder fields (`Retrieval`/`Summary`/`Context`/`History`) are populated, not left `.Empty`/null in production paths**
- [ ] **Behavioral: any approval/validation method branches on real input, not a hardcoded constant**
- [ ] **Behavioral: every `Subscribe`/`Open`/`Acquire`/`Create` has a matching `Unsubscribe`/`Close`/`Release`/`Dispose` on ALL exit paths (happy / catch / cancel), enforced by `finally` or `using` — missing on any path = P1 silent leak**
- [ ] **Behavioral: any Singleton holding a grow-only collection has a removal / cleanup / bounded-cap path — a collection that only grows is a permanent memory leak (P1)**

**Mutable shared state this phase:**
- [ ] (class/field) — protection mechanism: ___
- [ ] (class/field) — protection mechanism: ___

---

## 8. Cross-Cutting Infrastructure

Verify API infrastructure is complete.

- [ ] New controllers use MediatR (not direct Application service calls)
- [ ] New commands marked with `ICommand<T>` (triggers SaveChanges)
- [ ] New queries NOT marked with `ICommand<T>` (skips SaveChanges)
- [ ] New domain entities implement `ITenantScoped` (if multi-tenant)
- [ ] New aggregate roots implement `IAggregateRoot` with `_domainEvents`
- [ ] All async methods pass `CancellationToken`
- [ ] All implementation classes marked `internal sealed`
- [ ] All public service classes marked `public sealed`
- [ ] All method parameters have null guards (`ArgumentNullException.ThrowIfNull`)
- [ ] All string comparisons use `StringComparison.Ordinal`
- [ ] New API endpoints return DTOs (not Domain entities)
- [ ] `[Required]` on all required API model fields
- [ ] `ProblemDetails` registered for error responses
- [ ] CORS configured for new endpoints
- [ ] Health Checks include new external dependencies (Redis, etc.)
- [ ] `CorrelationId` stored in `HttpContext.Items`
- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] `dotnet test` — all passing
- [ ] Long-lived / streaming resources (SSE, WebSocket, Channel, IDisposable, HostedService) have a test for the disconnect/cancel/exception exit path (not just the happy path)

---

## Incremental Gate Sequence

Define the module order for this phase. Each module must pass all gates before the next starts.

```
Module 1: (name)
  - [ ] Code written
  - [ ] dotnet build 0 warnings
  - [ ] dotnet test all green
  - [ ] DI registration verified
  - [ ] DDD layer placement verified
  - [ ] EF Core mapping written (if applicable)
  - [ ] BDD scenario green

Module 2: (name)
  - [ ] ... (same gates)

Module N: (name)
  - [ ] ... (same gates)
```

## Final Regression

After all modules complete:
- [ ] Full `dotnet build` — 0 warnings, 0 errors
- [ ] Full `dotnet test` — all passing
- [ ] End-to-end path verified manually (one complete user journey)
- [ ] No new P0/P1 audit findings
- [ ] Blueprint updated if any deviations found
- [ ] Phase file retrospective filled in
