# Review Checklist by Module Type

Concrete, answerable checks. Every check must produce a specific answer, not "looks OK".

---

## Section A: State Machine Module

### State Reachability
- List all defined states. For each state, list all inbound transitions and all outbound transitions.
- Are any states unreachable (no inbound transitions)? If yes, is this intentional (initial state) or a bug?
- Are any states missing exit transitions (dead-end states)? If yes, is this intentional (terminal state) or a bug?
- Is there an initial state? Is it clearly defined?
- Is there a terminal/completed state? Can the machine always reach it?

### Retry Logic
- For each retry path: what is the max retry count? Is it from configuration or hardcoded?
- After max retries: what happens? Is there a rollback? Does the rollback undo side effects of previous steps?
- **Rollback target vs blueprint**: Compare the rollback target against the blueprint (e.g. appendix C.6). Rolling back ALL completed steps to Pending (full reset / whole-pipeline rerun) when the blueprint specifies rollback to a SPECIFIC step is P1 'rollback semantics contradict blueprint'. Verify the rolled-back set matches the intended target, not the entire workflow.
- Can retry create an infinite loop? Trace the path: retry -> fail -> retry -> fail. Is there a guaranteed exit?
- Does retry reset the step's internal state, or does it continue from the failure point? Is this intentional?
- Is the retry counter persisted? If the process crashes and resumes, does the counter reset to 0?

### Branching
- For each branch condition: what happens if the condition is neither true nor false (e.g., null, default, exception)?
- Are branch conditions based on state, input, or external data? Can external data change between check and action?
- Is there a default/else branch for every conditional?

### Concurrency
- Can two instances of the same workflow run concurrently? If yes, do they share mutable state?
- Is state transition atomic? Can a transition be interrupted mid-way?
- If state is persisted to DB, is there optimistic concurrency control (RowVersion / concurrency token)?

### Cancellation
- Does the state machine accept CancellationToken? Does it check it at each step?
- If cancelled mid-step, is the state left in a recoverable state? Can the workflow resume from the last completed step?
- **Crash recovery (mandatory assertion)**: Execution state must survive a process restart — a kill+restart mid-workflow must resume from the last completed step, not lose in-flight workflows. If state lives only in an in-memory collection (e.g. ConcurrentDictionary) with no per-step DB/Redis persistence, flag P1 'crash recovery not implemented despite documentation claim'. Require an integration test that kills and restarts the host mid-workflow and asserts resume.

### Behavioral Invariant Checks (mandatory — require reading method BODIES, not just signatures)

These catch "capability present but behavior wrong" drift. They MUST be answered by reading the actual loop/branch body, never by confirming the method exists. (Rollback-target and crash-recovery invariants are above; the checks below are additional.)

- **Resume continuity (P1)**: After a partial run, re-invoking Run/Resume MUST NOT re-execute steps already in a `Completed` state. Read the orchestration loop: it must skip `step.State == Completed` (or equivalent). A loop that iterates ALL steps unconditionally is a P1 finding — resume == full rerun.
- **External Pause responsiveness (P1)**: `PauseAsync` sets a flag/state; the execution loop MUST observe `workflow.CurrentState` / `IsPaused` at EVERY step boundary, not only `CancellationToken`. If the loop only checks `ct`, mid-step interruption is impossible — flag P1 if the blueprint requires interruption, else P2.
- **Retry actual count (P1)**: Actual attempts MUST equal configured `maxRetries`. Count iterations and verify the boundary is `<`, not `<=`. `while (retryCount <= maxRetries)` runs N+1 times → P1 off-by-one.
- **Retry quality (P2)**: Retry MUST skip non-transient errors and apply backoff. A retry that re-throws on every failure type, or has zero delay, is a P2.

---

## Section B: EF Core Migration Module

### Migration Safety
- Read the generated migration file. Does it DROP any table or column? If yes, is data loss intended and documented?
- Does the migration rename columns? Does it handle existing data in the old column?
- Can the migration run on a database with existing Phase 1 data without errors?
- Is there a rollback migration (Down method)? Does it correctly undo all Up changes?

### Mapping Completeness
- For each new or modified entity: is there an IEntityTypeConfiguration class?
- For each value object: is it mapped with OwnsOne or OwnsMany?
- For each collection property: is UsePropertyAccessMode(Field) used if the collection is IReadOnlyList?
- For each shadow property: is ValueGeneratedOnAdd() specified?
- For each column that might conflict (same name in different entities): is HasColumnName used to disambiguate?
- Are there any properties relying on EF Core default convention mapping? List them and verify they work correctly.

### Value Object Design
- Is the value object immutable (record or record struct)?
- Are equality operators defined if the value object is used in comparisons?
- Does the value object validate its invariants in the constructor?

---

## Section C: Multi-Agent / Orchestrator Module

### Agent Role Completeness
- List all defined agent roles. Do they match the blueprint's 6 roles (Requirement, Product, Architecture, Dev, Test, Docs)?
- Does each agent have a system prompt? Is the prompt specific to the role?
- Does each agent have a model assignment? Is it from configuration?
- Can a user create a custom agent role? If yes, trace the creation path. Does it persist?

### Message Flow
- Trace the complete message flow: Requirement -> Product -> Architecture -> Dev -> Test -> Docs.
- At each step: what does the agent receive as input? What does it produce as output?
- Is the output of one agent correctly passed as input to the next?
- What happens if an agent produces empty output? Does the next agent handle it?
- What happens if an agent throws an exception? Does the orchestrator catch it? Does it retry or fail the pipeline?

### Termination
- What is the termination condition for the group chat? Is it based on message count, content, or external signal?
- Can the termination condition trigger prematurely (before all agents have participated)?
- Can the termination condition never trigger (infinite conversation)?
- Is there a max-rounds fallback?

### Context Propagation
- How is context (conversation history, project info) passed between agents?
- Is the context serialized? Can it exceed token limits?
- Is the context truncated if too long? Does truncation lose critical information?
- **Context scaling implementation (P2)**: If the blueprint specifies context windowing / summarization / retrieval-augmentation (e.g. appendix C.3.1), verify the scaling mechanism is actually implemented — the scaling fields MUST be populated, not left `.Empty`. "Context propagation exists" is NOT sufficient; the scaling feature itself must be present. Flag P2 if the mechanism is a placeholder.
- **Preset / role detection robustness (P2)**: If the orchestration preset/role is detected via string sniffing of prompts or class names, flag the fragility and the risk that it breaks Resume's stored preset (re-sniffing may disagree with the persisted value). Prefer an explicit preset field carried in the workflow state.

### API Verification
- WebFetch the AutoGen.NET documentation for: IAgent, GroupChat, Termination, message passing API.
- Compare each API call in the code with the documented signature.
- Flag any mismatch in parameter names, types, or return types.

### Implementation Fidelity (mandatory)

- **Framework symbol-presence check (generalized)**: Grep the module for concrete API symbols of the framework the class name implies. Examples: a class named `*AutoGen*` / referencing `AutoGenSettings` → AutoGen.NET symbols (`GroupChat`, `AssistantAgent`, `GroupChatManager`, `Message`); a class named `*Redis*` → StackExchange.Redis symbols; `*Ef*`/`*Repository*` → EF Core `DbContext`/LINQ symbols. If a class whose name implies framework X contains ZERO such symbols, flag P1 'hollow/misnamed class — does not use X; implementation is a manual substitute or dead code'. Positive symbol presence is required; do NOT rely on WebFetch alone.

- **Stub / simulator definition (tightened)**: A public method on a registered (or live-path) implementation is a P1 STUB when ANY of:
  - its result does NOT depend on real input (always returns the same constant: `true`, `APPROVED`, `Success`, canned data);
  - its key branch never triggers regardless of input;
  - it performs a *simulated* action (sets a fake APPROVED, returns hardcoded data, logs "review passed") instead of real computation.
  A stub is P1 **even when it "does something"** (sets state, logs, delegates) — activity ≠ effectiveness. ONLY acceptable if the blueprint EXPLICITLY designates it as a placeholder; in that case it MUST still be recorded as an **open P1** (not waived) per `ddd-code-reviewer` Auto-Fix Rule.

- **Placeholder field check (P1)**: Fields named like `Retrieval` / `Summary` / `Context` / `History` / `Scratch` MUST be non-empty in any production path the blueprint says must populate them. A `.Empty` / `null` / unused instance in such a path is a P1 STUB (feature not implemented), not a "default".

- **Target reference resolution (P2)**: If the blueprint says component X produces a target/hint consumed by component Y (e.g. critic → rework target), verify the produced value is actually READ by Y's loop. An always-null / unreferenced target = P2 'feature wired but never consumed'.

---

## Section D: Redis / Cache Module

### Connection Management
- Is IConnectionMultiplexer registered as Singleton?
- Is the connection string from configuration (not hardcoded)?
- What happens when Redis is unavailable? Is there a fallback to InMemory?
- Is the fallback tested? Does it degrade gracefully (log warning, continue)?

### Serialization
- What serialization format is used (JSON, MessagePack, raw bytes)?
- Can the serialized data round-trip correctly (set then get returns the same object)?
- Are there type-serialization issues (polymorphism, DateTime formats, enum values)?

### Expiry
- How is expiry set? Is it TimeSpan or absolute expiration?
- What happens when data expires? Does the consumer handle the "not found" case?
- Is expiry time from configuration?

### Concurrency
- Are Redis operations atomic? Are there read-then-write patterns that need transactions?
- Is there a race condition between get and set (stale read)?

---

## Section E: Domain Event / Pipeline Behavior Module

### Event Ordering
- Are domain events collected from aggregate roots BEFORE SaveChangesAsync?
- Is the event collection and dispatch inside the same transaction scope?
- What happens if an event handler throws? Does it block SaveChanges? Should it?
- Are events dispatched synchronously or asynchronously? Is this intentional?

### Handler Registration
- For each domain event class: is there a corresponding INotificationHandler registered?
- Are handlers registered in the correct DI container (Application or Infrastructure)?
- Can a handler be missed (event published but no handler)? What happens?

### Event Design
- Does each event carry enough information for the handler to act (aggregate ID, timestamp, payload)?
- Are events named consistently (past tense: Created, Completed, Failed)?
- Is the event granularity appropriate (not too coarse, not too fine)?

---

## Section F: Repository / Data Access Module

### Interface Completeness
- Does the repository interface cover all query methods needed by the application layer?
- Are there methods that return IQueryable (leaking data access concerns)?
- Do all methods accept CancellationToken?

### Query Correctness
- Do query methods apply the tenant query filter (if multi-tenant)?
- Are there N+1 query patterns (loading a list then querying each item in a loop)?
- Are Include/ThenInclude calls used correctly for eager loading?

### Transaction
- Is the repository using the same DbContext as UnitOfWork?
- Are there methods that call SaveChanges directly (bypassing UnitOfWork)?

---

## Section G: API Controller / Endpoint Module

### MediatR Usage
- Does the controller inject ONLY IMediator and IOptions<T>? (Not direct Application services)
- Does each action send a Command or Query via IMediator?
- Are Commands marked with ICommand<T>?
- Are DTOs returned (not Domain entities)?

### Validation
- Are [Required] attributes on all required fields?
- Is there model validation? Does the controller check ModelState?
- Are input boundaries validated (max length, format, range)?

### Error Handling
- Does the controller let exceptions propagate to the global handler?
- Are there try-catch blocks in the controller that should be removed (global handler handles it)?

---

## Section H: Configuration / Settings Module

### Completeness
- Is the settings class defined with all needed properties?
- Is it registered with services.Configure<T>(section)?
- Is the section present in appsettings.json?
- Is the section present in appsettings.QuickStart.json (if applicable)?
- Are there default values? **For security-sensitive settings (signing/encryption keys, secrets, API keys) defaults MUST be non-empty and non-dev-default — an empty or dev-fallback default is a P1 (must-fix), not a soft 'sensible?' question.** For other settings, are defaults sensible?

### Consumption
- Are all properties consumed by at least one service?
- Are there unused configuration properties?
- Is IOptions<T> used (not IConfiguration direct access)?

---

## Section Z: General (applies to ALL modules)

### Null Safety
- List all method parameters. Which ones lack null guards?
- List all nullable reference types (string?). Are they handled with null checks or null-conditional operators?
- List all null! suppressions. Does each have a comment explaining why?

### CancellationToken
- List all async methods. Which ones lack CancellationToken parameter?
- List all await calls. Which ones don't pass CancellationToken to the awaited method?
- In MediatR pipeline behaviors: is next() called correctly for the MediatR version?

### Exception Handling
- List all try-catch blocks. Which ones have empty catch blocks (swallowing exceptions)?
- Which catch blocks catch Exception (too broad)? Should they catch specific exceptions?
- Are there catch blocks that only log without rethrowing? Is this intentional?

### DDD Compliance
- Are interfaces in Application.Abstractions or Domain.Abstractions?
- Are implementations in Infrastructure?
- Is DI registration in Infrastructure.DependencyInjection.cs?
- Does Application reference Infrastructure? (should not)
- Does Domain have any external NuGet dependency? (should not)

### Sealed / Internal
- Are Infrastructure implementation classes internal sealed?
- Are Application public services public sealed?
- Are settings classes sealed?

### String Comparison
- Are string comparisons using StringComparison.Ordinal (not culture-sensitive)?
- Are string equality checks using String.Equals (not == for non-constant strings)?

### Logging
- Do key operations have logging (entry, exit, error)?
- Do log messages include enough context (entity ID, operation name)?
- Are there log statements with string interpolation instead of structured logging templates?

### Hardcoded Values
- List all numeric literals in the code. Which ones should be configuration values?
- List all string literals. Which ones should be configuration values or constants?
- Are there hardcoded connection strings, URLs, or API keys?

### XML Documentation
- List all public types (classes, records, interfaces, enums). Which ones lack `/// <summary>`?
- List all public methods. Which ones lack `/// <summary>` and `<param>` tags?
- List all public properties on settings/config classes. Which ones lack `/// <summary>`?
- Are XML comments meaningful (not just restating the member name)?
- Do interface methods have `<param>` and `<returns>` tags where applicable?
- Are enum values documented if their meaning is not obvious from the name?
- **Chinese XML comments enforcement:**
  - For **new** code: do public types and members have Chinese XML comments containing Chinese characters (e.g. `中文`)?
  - Are interface methods documented with Chinese `<summary>` and `<param>` tags?
  - Check preceding lines of new public class/method declarations for `///` comments; are they in Chinese?
  - English-only comments are acceptable only when: (a) the member name is fully self-documenting, (b) the comment is a pure `<see cref="..."/>` reference, or (c) the member is in test code.
  - Pre-existing code without Chinese comments should be flagged but does not block the phase.

### Architecture Tests (Compile-time DDD Enforcement)
- Does the ArchitectureTests project have a test covering this module's DDD constraints?
- If the module adds a new aggregate root: is there a test verifying it has IEntityTypeConfiguration?
- If the module adds a new interface in Abstractions: is there a test checking DI registration?
- If the module adds a new Controller: is there a test checking it injects IMediator only?
- If the module adds a new Infrastructure class: is there a test checking it's internal sealed?
- Run `dotnet test src/AgentPlatform.ArchitectureTests` — does it pass?

### Integration Tests (Real Infrastructure)
- If the module uses Redis: is there a Testcontainers fixture for Redis?
- If the module uses PostgreSQL/PGVector: is there a Testcontainers fixture for PostgreSQL?
- If the module uses an external HTTP service: is there an integration test that starts the real dependency?
- Are integration tests gated by Docker availability in CI (not blocking PRs on non-Docker runners)?

### Security

> **Severity policy**: Every security-relevant finding from this section is **P1 (must-fix)**. Do NOT classify security gaps as P2/P3 or waive them for later.

- Run `dotnet list src/AgentPlatform.sln package --vulnerable` — any known CVEs?
- Does the module handle secrets (API keys, connection strings)? Are they from configuration, not hardcoded?
- If the module opens HTTP endpoints: is there input validation? (Request size limits, content-type checks)
- If the module executes user-provided code/config: are there sandbox/escape-prevention measures?
- **Secret/crypto key value check (P1, must-fix)**: For any signing/encryption key or secret loaded from configuration (e.g. `JwtSecretKey`, `AesEncryptionKey`, DB connection secrets), verify the value is NON-EMPTY and NON-DEFAULT (no dev-fallback) in non-Development. An empty config key that silently falls back to a hardcoded dev key is a **P1** security hole — require a real value or a fail-closed startup guard that throws when empty in Production.
- **RBAC least-privilege check (P1, must-fix)**: If authentication issues a role/claim, verify role assignment follows least privilege — no identity (API key, service account) is hardcoded to the highest role (e.g. always `Admin`). Flag **P1** any code path that assigns a fixed admin role regardless of the credential.
- **Input-validation over-breadth check (P1, must-fix)**: For every input-validation rule (regex / blocklist / size limit), verify it does NOT over-reject legitimate input. Require a NEGATIVE test proving a benign payload containing the matched tokens (e.g. brackets / code / JSON) is accepted. A validator that rejects all bracketed content is **P1** over-broad.
- **CORS policy — DEFERRED**: `AllowAnyOrigin` / empty `AllowedOrigins` is intentionally left as-is for now and will be changed manually later. Do NOT flag CORS misconfiguration in this review pass.

### Swagger / API Documentation
- Is Swashbuckle.AspNetCore (or Microsoft.AspNetCore.OpenApi) referenced in the Api .csproj?
- Is `AddSwaggerGen` (or `AddOpenApi`) called in Program.cs with proper configuration?
- Is `<GenerateDocumentationFile>true</GenerateDocumentationFile>` set in the Api .csproj?
- Is `<GenerateDocumentationFile>true</GenerateDocumentationFile>` set in the Application .csproj (for DTO docs)?
- Is `IncludeXmlComments` called for the Api and Application XML doc files in the SwaggerGen configuration?
- Is `UseSwagger()` and `UseSwaggerUI()` (or `MapScalarApiReference()`) present in the Development environment block?
- Do all controller action methods have `/// <summary>` comments that Swagger can display?
- Do action methods with parameters have `<param>` tags so Swagger shows parameter descriptions?
- Is the SwaggerDoc info (Title, Version, Description) populated with meaningful values?

### Behavioral Invariant Verification (mandatory for critical methods)

Beyond symbol/capability checks, assert each critical method's RUNTIME BEHAVIOR matches the blueprint's intent. Read the method body and verify:

- **Result depends on input**: For methods returning a status/result/approval enum or bool, the return value MUST branch on real computation. A method that always returns the same constant regardless of input is a P1 stub (see Section C stub definition) unless blueprint-sanctioned AND recorded as open P1.
- **Loop boundary correctness**: For any bounded loop with a counter vs a max, verify `<` vs `<=` matches intent; count the iterations. Off-by-one = P1.
- **CancellationToken threading**: A `CancellationToken` parameter MUST be passed to the inner `await` calls it guards. A `ct` declared but never forwarded to an awaited call is a P2 (token is decorative).
- **Lock discipline (all paths)**: Shared mutable state MUST be accessed under `lock` / `ConcurrentXxx` on EVERY read and write path — not only the write path. A read outside the lock is a P2 race.
- **Wiring vs dead code (蜜罐 detection)**: For every public/registered class, confirm it has a REAL call site OR is injected into ≥1 consuming class. A DI registration alone is NOT sufficient evidence of use — a service registered in DI (`AddSingleton`/`AddScoped`/etc.) but with zero injection sites and zero call sites anywhere in the codebase is DORMANT (still dead code). Flag **P1 (must-fix)** and either wire it into a real consumer or mark `[Obsolete]`; a class whose name implies a framework/live path but is never the one actually invoked is a misleading 蜜罐. **Wiring/dead-code findings are P1 — do NOT downgrade to P2/P3.
- **Enum-member / declared-constant dead code**: For every enum member and `const`/declared constant in the module, grep the WHOLE solution for its reference sites. A member that is **ZERO-referenced** anywhere (only its own declaration exists) is DORMANT — flag **P1 (must-fix)**: either wire it into a real emit/call site (e.g. `AuditLog.Record(AuditActionType.KeyRotation, ...)`) or mark the member `[Obsolete]` with rationale. This catches blueprint-promised audit/state values that were defined but never written. **P1 — do NOT downgrade to P2/P3.****

---

## Section H2: Resource Lifecycle / Acquire-Release Symmetry

Applies to ANY class that acquires a subscribable, disposable, or long-lived resource. Run it for streaming endpoints (SSE/WebSocket), Channel subscriptions, `IDisposable`/`IAsyncDisposable` holders, Timer/HostedService state, and Singletons holding grow-only collections. This section catches "half-finished fixes" where a cleanup API exists but is never invoked — the structural gate passes on symbol presence; this section verifies the call graph.

### Acquire/Release Symmetry (P1)
- For every `Subscribe` / `Open` / `Acquire` / `Allocate` / `Create` / `Add` call that returns a handle (subscriberId, reader, connection, lease, registration):
  - Is there a matching `Unsubscribe` / `Close` / `Release` / `Dispose` / `Remove` / `Deregister`?
  - Is it called on **EVERY exit path**: happy completion (`break`/`return`), `catch` (including `OperationCanceledException`), and `CancellationToken` cancellation?
  - Is the release wrapped in `finally` (or `using` / `await using`) so it runs even on exception / cancel?
  - A release that exists but is missing on ANY exit path → P1 silent leak. (Real example: `var (_, reader) = _broadcaster.Subscribe(id)` discards the subscriberId; neither `catch` nor `break` calls `Unsubscribe` → channel leaks forever in a Singleton dict.)
- Flag `P1` if the handle is acquired but no release is reachable on all exit paths.

### Method-level Dead Code (P2)
- Grep the whole solution for each public cleanup/release method declared on an interface or live-path class (e.g. `Unsubscribe`, `Close`, `Release`, `Dispose`).
- A cleanup method with **ZERO call sites** anywhere in the codebase is DEAD CODE at the method level — distinct from an unreferenced class. It is a "finished-looking fix" that nothing wires up.
- Require: either wire it into the release path (`finally`/`using`), or mark it `[Obsolete]` with a rationale. Do NOT let it sit implemented-but-unused.

### Discarded Return Value (P2)
- Scan for `var (_, x) = Acquire()` / `(_, reader) = Subscribe()` / `var _ = Open()` patterns where the discarded element is the release handle.
- If the class exposes a release method that takes that handle as an argument, discarding it is almost certainly a leak → P2 (escalate to P1 if no other path holds the handle).
- The `_` discard hides "we got a value we need for cleanup" — never assume a discard is safe on a resource-owning class.

### Singleton × Grow-only Collection (P1)
- If a service is registered as **Singleton** and holds a `Dictionary` / `List` / `ConcurrentDictionary` / `HashSet` / `Queue` that entries are ADDED to but NEVER REMOVED:
  - Is there a documented removal path (on disconnect / completion / cancellation)?
  - Is there a bounded cap or periodic cleanup (LRU / TTL / sweep)?
  - If the collection only grows and nothing removes → flag `P1` "permanent memory leak"; the entry lives for the process lifetime.
  - A `ConcurrentDictionary` satisfies thread-safety but does NOT satisfy lifecycle — do not let "uses ConcurrentXxx" mask a missing removal path.

### Test Coverage for Long-lived Resources (P2)
- For any streaming / disposable / hosted resource module, confirm a test exists for the **disconnect / cancel / exception exit path** (not just the happy publish path).
- No such test → flag `P2` coverage gap. A green suite with only the happy path gives false confidence that resource cleanup works.
