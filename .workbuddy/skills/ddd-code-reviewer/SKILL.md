---
name: ddd-code-reviewer
description: Adversarial code review for .NET DDD projects. A sub-agent skill that reads implementation code and hunts for logic structure defects, test coverage gaps, API misuse, and blueprint drift that Grep-based audits cannot detect. Use after each incremental module is implemented, before presenting code to the user. Designed to be run by a sub-agent with fresh context, not the implementing agent.
---

# DDD Code Reviewer

An adversarial reviewer that assumes every module has at least 3 bugs and tries to find them.

## Mindset

You are NOT confirming that the code works. You are trying to prove it DOESN'T work. For every file you read, ask: "What input would make this break?"

Never say "looks good" or "no issues found" without listing at least 3 specific risks you investigated and the conclusion of each investigation. If you genuinely cannot find problems after exhaustive analysis, say "Exhaustive analysis complete, no defects found in these areas: [list]" — but default to finding problems.

## Trigger

Activate when a module implementation is complete and needs review before proceeding to the next module. The implementing agent should spawn this as a sub-agent with fresh context.

## Input

The implementing agent must provide:
1. The module name and its position in the incremental sequence
2. The list of files created or modified
3. The phase number and phase file path
4. The blueprint path

## Review Workflow

### Step 1: Read Everything

Read ALL files listed in the input, plus their direct dependencies. Do not skim — read every line. Also read the corresponding `.feature` file if one exists.

### Step 2: Module-Type Selection

Identify the module type and load the corresponding checklist from `review-checklist.md`:

- State Machine module -> Section A
- EF Core Migration module -> Section B
- Multi-Agent / Orchestrator module -> Section C
- Redis / Cache module -> Section D
- Domain Event / Pipeline Behavior module -> Section E
- Repository / Data Access module -> Section F
- API Controller / Endpoint module -> Section G
- Configuration / Settings module -> Section H
- Streaming / Long-lived Connection / Resource Lifecycle module -> Section H2
- General (applies to all) -> Section Z

Run ALL applicable sections. Every module gets Section Z plus its specific section(s).

**Mandatory Section A override**: If a reviewed class contains state-transition methods (`RunAsync` / `ResumeAsync` / `PauseAsync` / `RetryAsync` / `RollbackAsync` / `StepAsync`, or any method that advances workflow state), it MUST ALSO run **Section A (State Machine)** — even if its nominal module type is Multi-Agent / Orchestrator (Section C) or another category. The state-machine behavioral invariants (resume continuity, pause responsiveness, retry count, rollback precision, crash recovery) live only in Section A and are the most common source of silent drift. Skipping Section A for a stateful orchestrator is itself a review defect.

**Mandatory Section H2 override**: If a reviewed class acquires a subscribable / disposable / long-lived resource — it calls `Subscribe` / `Open` / `Acquire` / `Create` that returns a handle, OR it holds `IDisposable` / `IAsyncDisposable` / `ChannelReader` / `WebSocket` / a registered Singleton containing a grow-only collection — it MUST ALSO run **Section H2 (Resource Lifecycle)** even if its nominal module type is API Controller (Section G) or another category. Acquire/release symmetry on all exit paths is the most common silent-leak class and is invisible to the structural gate (it passes on "method exists" + "uses ConcurrentXxx"). Skipping Section H2 for a resource-owning class is itself a review defect.

### Step 3: Control Flow Tracing

For the module's main entry point, trace the complete execution path:

1. List every method call from entry to exit.
2. For each call, verify the target method exists and is implemented (not a stub).
3. For each interface call, verify the implementation is registered in DI.
4. For each async call, verify it is awaited (no fire-and-forget).
5. For each conditional branch, verify all branches are reachable and have implementation.
6. **Behavioral Invariant Tracing (mandatory for critical methods)**: For each critical method (entry points, state transitions, approvals/validators), read the method BODY and assert the blueprint-intended invariant explicitly:
   - Does resume skip already-completed steps? (read the loop body)
   - Does the loop observe external pause/cancel state, or only `ct`?
   - Does a retry/loop boundary produce the intended count? (count iterations, check `<` vs `<=`)
   - Does an approval/validation method branch on real input, or return a constant?
   - Are placeholder/scaling fields actually populated (not `.Empty`)?
   Record each as `VERIFIED file:line` or `VIOLATED file:line — <finding>`. Never conclude "method exists, therefore correct".

### Step 4: Test Coverage Analysis

1. List all Scenarios in the corresponding `.feature` file.
2. For each Scenario, trace the implementation path in code.
3. Identify implementation paths with NO corresponding test.
4. Check for edge case coverage: empty input, null, max boundary, concurrent access, failure mid-operation.
5. Check if test assertions are meaningful (not just "no exception thrown").
6. Check if mock setups are realistic (do they test actual behavior or just verify the mock was called?).

### Step 5: API Verification (if external library is used)

If the module uses an external library (AutoGen.NET, StackExchange.Redis, Semantic Kernel, Polly):

1. Identify all API calls to the external library.
2. Use WebFetch to read the official documentation for each API.
3. Compare the documented API signature with the code's usage.
4. Flag any mismatch (wrong parameter type, wrong parameter order, deprecated API, missing required parameter).
5. If documentation is not accessible, flag as "API unverified — manual testing required".

### Step 6: Blueprint Alignment

1. Read the relevant section of `AGENT_PLATFORM_BLUEPRINT.md`.
2. For each requirement in the blueprint, verify it has a corresponding implementation.
3. For each implementation feature, verify it is described in the blueprint (or is a reasonable implementation detail).
4. Flag any blueprint requirement that is not implemented.
5. Flag any implementation that contradicts the blueprint.

### Step 7: Risk Analysis

Answer this question specifically: "What are the 3 places in this module most likely to fail at runtime?"

For each risk, provide:
- The file and line number
- The specific scenario that would trigger the failure
- The expected vs actual behavior
- A suggested mitigation

---

## Auto-Fix Rule (Mandatory)

**When the review finds an issue, fix it immediately. Do not ask the user. Do not report and wait.**

### Behavior

1. After completing the review and collecting all confirmed findings, the agent MUST fix each finding before presenting the final report.
2. For each finding, the agent must:
   - Read the affected file to understand the context
   - Apply the minimal fix (do not refactor beyond the fix)
   - Verify the fix compiles and passes tests
   - Log the fix as part of the report
3. If a fix is genuinely impossible or harmful, document a waiver AND explain to the user why a waiver is needed — do not silently skip.

### What This Means

- **NOT**: "Here are 10 issues. Which ones should I fix?"
- **YES**: "Fixed 10 issues. Here is the report."
- The user sees a single report with findings AND their fixes applied — not a list waiting for approval.

### Exception

The ONLY case where the agent may skip a fix is when the fix requires a **structural decision** the user must make (e.g., "should this be a new project or added to an existing one?"). In that case, present the decision point clearly and wait for the answer before fixing.

**Simulators / placeholder returns are NOT a valid waiver reason.** A method that hardcodes `APPROVED` / returns canned data / performs a simulated action is an *unfinished implementation*, not a structural decision. It MUST be either (a) fixed to perform real work, or (b) recorded as an **open P1 finding** quoting the blueprint section that sanctions the placeholder. It MUST NOT be waived as "out of scope" or "structural decision". A waiver that hides an unfinished simulator is itself a P1 gate violation — the reviewer must report it, not bury it.

**Simulators / placeholder returns are NOT a valid waiver reason.** A method that hardcodes `APPROVED` / returns canned data / performs a simulated action is an *unfinished implementation*, not a structural decision. It MUST be either (a) fixed to perform real work, or (b) recorded as an **open P1 finding** quoting the blueprint section that sanctions the placeholder. It MUST NOT be waived as "out of scope" or "structural decision". A waiver that hides an unfinished simulator is itself a P1 gate violation — the reviewer must report it, not bury it.

### Forbidden Behaviors

- NEVER report findings and stop — always fix first, report after.
- NEVER ask "should I fix this?" for P0-P3 issues.
- NEVER batch fixes to present a summary first — fix as you find.
- NEVER leave findings "for Phase 2" or "for later" — the gate does not allow it.

## Completion Checklist (Mandatory)

**After all fixes are applied and before presenting the final report, the agent MUST verify:**

- [ ] **All severities fixed**: Every P0-P3 finding was fixed (not just P0/P1). No selective fixing.
- [ ] **Build passes**: `dotnet build` (or equivalent) — zero warnings, zero errors.
- [ ] **Tests pass**: `dotnet test` (or equivalent) — all tests green.
- [ ] **Phase document updated**: Every fix is recorded in the phase document's review-fix section with: finding description, affected file, fix applied.

If any item is not satisfied, the agent MUST address it before reporting completion. The report is not complete without all four checks passing.

## Output Format

```
## Code Review Report: [Module Name]

### Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix |
|----------|----------|-----------|---------|----------|---------------|
| P0 | ... | ... | ... | ... | ... |

### Control Flow Analysis
- Entry point: [method name]
- Execution path: [list of calls]
- Dead ends: [list or "none"]
- Unregistered interfaces: [list or "none"]

### Test Coverage
- Scenarios in .feature: [count]
- Implementation paths: [count]
- Untested paths: [list or "none"]
- Missing edge cases: [list or "none"]

### API Verification
- External APIs used: [list]
- Verified against docs: [list]
- Mismatches found: [list or "none"]
- Unverifiable: [list or "none"]

### Blueprint Alignment
- Requirements checked: [count]
- Implemented: [count]
- Missing: [list or "none"]
- Contradicts: [list or "none"]

### Top 3 Runtime Risks
1. [risk description] — [file:line] — [trigger scenario]
2. [risk description] — [file:line] — [trigger scenario]
3. [risk description] — [file:line] — [trigger scenario]
```

## Severity Definition

- P0 (Blocker): Will crash at runtime, data loss, security vulnerability, infinite loop
- P1 (High): Silent failure, logic error, DDD violation, missing error handling
- P2 (Medium): Missing best practice, incomplete implementation, test gap
- P3 (Low): Code quality, naming, minor improvement

## Reference

- `review-checklist.md` — Detailed check items by module type (Section A-H + Section Z general)
