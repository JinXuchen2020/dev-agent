---
name: ddd-phase-quality-gate
description: Quality gate for .NET DDD incremental phase development. Generates phase-specific checklists (embedded INSIDE the phase document, never as a separate file) and audits codebase for DI registration gaps, DDD layer violations, EF Core mapping defects, hardcoded values, missing CancellationToken, concurrency issues, and API infrastructure gaps. Use when starting a new phase, opening a phase-N-*.md file, or when the user asks to audit code quality, verify phase readiness, or review DDD compliance in a .NET project.
---

# DDD Phase Quality Gate

Two modes: **checklist** (generate/refresh phase checklist) and **audit** (scan codebase for violations). Both can run together.

## Trigger Detection

Auto-activate when any of these occur:
- User opens or references a `phases/phase-N-*.md` file
- User says "start phase N", "开始阶段N", "Phase 2 开始", or similar
- User asks to "audit", "check quality", "审查代码质量", "检查 DI 注册"
- User asks to verify readiness before starting a new phase

If triggered by a phase file, read that file first to identify the phase number, its tasks, and acceptance criteria.

## Mode 1: Checklist Generation

### Steps

1. Read the phase file (e.g. `phases/phase-2-multi-agent.md`) to identify tasks and acceptance criteria.
2. Read `general-checklist.md` from this skill directory for the universal checklist template.
3. Identify the phase document the user referenced/opened (e.g. `phases/phase-3-platformization.md`). **Never create a separate `phases/phase-N-checklist.md` file.** The quality checklist is embedded as a section inside this phase document. If the document already has a `## Quality Gate Checklist` (or otherwise consolidated) section, refresh it in place; otherwise append it.
4. Read `phase-2-checklist.md` from this skill directory if the active phase is Phase 2. For other phases, use `general-checklist.md` as the base and adapt task items from the phase file.
5. Write the generated checklist into the referenced phase document as a `## Phase N Quality Gate Checklist` section (append if missing, otherwise update in place). **Do not create any new file.**
6. Present a summary of the checklist categories and item count to the user.

### Checklist Categories (always include all 8)

1. **Pre-flight Version Audit** — NuGet package versions locked, API signatures verified
2. **BDD Scenarios First** — SpecFlow .feature files written before implementation
3. **DDD Layer Rules** — interface location, implementation location, DI registration location
4. **DI Registration Completeness** — every new interface has a registered implementation
5. **Configuration-First** — all variable values go through IOptions<T>
6. **EF Core Mapping Sync** — every aggregate/VO has IEntityTypeConfiguration
7. **Concurrency & Lifecycle** — mutable shared state reviewed, DI lifetime correct
8. **Cross-Cutting Infrastructure** — CORS, Health, Exception handling, ProblemDetails, etc.

### Incremental Gate Rule

Each checklist must define an incremental sequence: complete one module -> compile (0 warnings) -> test (all green) -> DI audit -> layer audit -> next module. List the specific modules for the active phase.

## Mode 2: Codebase Audit

### Steps

1. Read `audit-patterns.md` from this skill directory for Grep/Glob patterns.
2. Run each audit pattern against the project source code.
3. Collect findings, classify by severity:
   - **P0 (Blocker)**: Won't compile, runtime crash, data loss risk
   - **P1 (High)**: Silent failure, security gap, DDD violation
   - **P2 (Medium)**: Code quality, missing best practice
   - **P3 (Low)**: Style, naming, minor cleanup
4. Present findings as a table: `| Severity | Pattern | File | Finding | Fix |`
5. If P0/P1 issues exist, recommend running the checklist mode to prevent recurrence.

### Audit Categories (scan all 12)

| Category | What to Detect |
|----------|---------------|
| DI Registration Gaps | Interface in Application.Abstractions but not registered in Infrastructure/DI |
| DDD Layer Violations | Implementation class in Application layer, interface in Infrastructure |
| EF Core Mapping Gaps | Aggregate root without IEntityTypeConfiguration |
| Hardcoded Values | Magic numbers, hardcoded GUIDs/strings in non-trivial code |
| Missing CancellationToken | async methods without CancellationToken parameter |
| Missing Modifiers | Implementation classes without internal sealed |
| Concurrency Risks | static/Singleton with mutable state without lock/ConcurrentXxx; OR a Singleton holding a grow-only collection (Dictionary/List/ConcurrentDictionary/etc.) with NO removal/cleanup path — permanent memory leak |
| Missing Null Guards | Public method parameters without null check |
| API Infrastructure | Missing CORS/HealthChecks/ExceptionHandler/ProblemDetails |
| Blueprint Drift | Blueprint features described but not implemented (marked as future) |
| Missing XML Documentation | Public types/members without /// <summary> comments |
| Swagger / API Documentation | Missing Swashbuckle/OpenAPI, SwaggerGen, XML doc file gen, IncludeXmlComments |
| Dead Code / Misnamed Hollow Class | class name implies framework X but contains zero X symbols; placeholder fields left `.Empty` in production path; unreferenced implementation class (dead code / 蜜罐); **method-level: a public cleanup/release API (Unsubscribe/Close/Release/Dispose) declared on an interface or live-path class with ZERO call sites anywhere in the codebase**; **enum/const dead code: an enum member or declared constant with ZERO reference sites anywhere in the codebase is DORMANT — P1, wire it into a real emit/call site or mark `[Obsolete]` (catches blueprint-promised audit/state values defined but never written)** |

### Audit Execution

Use Grep and Glob tools to scan the project. Key patterns are in `audit-patterns.md`. Run patterns in parallel where possible. For each finding, read the relevant file to confirm before reporting.

## Gate Rule (Mandatory)

The quality gate is a **gate**, not a report. After running an audit, the following rules are mandatory:

### Fix Requirements

| Severity | Action Required |
|----------|----------------|
| P0 (Blocker) | MUST fix immediately. No exceptions, no waivers. |
| P1 (High) | MUST fix before proceeding to the next phase. No exceptions, no waivers. |
| P2 (Medium) | MUST fix before proceeding to the next phase. If a fix is genuinely impossible or harmful, an explicit waiver MUST be documented with: (1) the reason, (2) the risk accepted, (3) the target phase for resolution. |
| P3 (Low) | MUST fix. If a fix is not worth the effort, an explicit waiver MUST be documented with the reason. No silent skipping. |

### Gate Status

After all fixes are applied (or waivers documented), the audit MUST be re-run to verify:

```
Gate Status: PASS  (when P0=0, P1=0, P2=0 or all-waived, P3=0 or all-waived)
Gate Status: BLOCKED (when any P0/P1/P2 remains unfixed and unwaived)
```

### Forbidden Behaviors

- NEVER mark a finding as "pre-existing" to skip fixing it. If the skill found it, it must be fixed or waived.
- NEVER mark a finding as "acceptable for Phase N" to skip fixing it. The audit does not care about phase scope — it cares about code quality.
- NEVER present the work as "done" or "complete" while the Gate is BLOCKED.
- NEVER proceed to the next phase, next module, or next task while the Gate is BLOCKED.
- If the audit finds issues in code you just wrote, fix them before reporting completion. Do not leave them for "later".

### Waiver Format

Each waiver must be recorded as:

```
| Severity | File | Finding | Waiver Reason | Risk Accepted | Target Phase |
```

Waivers are reviewed at phase boundaries. Any waiver not resolved by its target phase becomes a P1.

---

## Auto-Fix Rule (Mandatory)

**When the audit finds an issue, fix it immediately. Do not ask the user. Do not report and wait.**

### Behavior

1. After running an audit and collecting all confirmed findings, the agent MUST fix each finding before presenting the final report.
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

### Forbidden Behaviors

- NEVER report findings and stop — always fix first, report after.
- NEVER ask "should I fix this?" for P0-P3 issues.
- NEVER batch fixes to present a summary first — fix as you find.
- NEVER leave findings "for Phase 2" or "for later" — the gate does not allow it.

## Mode 3: Combined (checklist + audit)

When both modes are requested:
1. Run audit first to establish a baseline of existing issues.
2. Generate checklist with audit findings noted as "pre-existing issues to resolve".
3. Present both: current violations + prevention checklist for the new phase.

## Reference Files

- `general-checklist.md` — Universal 8-category checklist template, applicable to all phases
- `phase-2-checklist.md` — **Template only** (lives in this skill directory). Phase 2 specific items to merge into the phase document's checklist section.
- `audit-patterns.md` — Grep/Glob patterns for each audit category

## Output Format

When presenting results, use this structure:

```
## Phase N Quality Gate Report

### Gate Status: PASS or BLOCKED
[P0: x | P1: x | P2: x (waived: x) | P3: x (waived: x)]

### Mode: Audit
[Findings table if audit was run]

### Waivers
[Waiver table if any waivers exist, otherwise "None"]

### Mode: Checklist
[Generated checklist summary with item counts per category]

### Recommendation
[Next steps based on findings — if BLOCKED, list what must be fixed]
```

## Completion Checklist (Mandatory)

**After all fixes are applied (or waivers documented) and before presenting the final report, the agent MUST verify:**

- [ ] **All severities fixed or waived**: Every P0-P3 finding was fixed, or has a documented waiver. No selective skipping.
- [ ] **Build passes**: `dotnet build` (or equivalent) — zero warnings, zero errors.
- [ ] **Tests pass**: `dotnet test` (or equivalent) — all tests green.
- [ ] **Phase document updated**: Every fix is recorded in the phase document's review-fix section with: finding description, affected file, fix applied.

If any item is not satisfied, the agent MUST address it before reporting completion. The report is not complete without all four checks passing.

## Important Rules

- Never skip audit categories. Run all 12 even if some return empty.
- Always read the actual file before reporting a finding. Grep matches may be false positives.
- Report file paths relative to the project root, not absolute paths.
- If the project is not a .NET DDD project, inform the user and skip the skill.
- The quality checklist is written **into the referenced phase document** (as a section), never as a separate `phases/phase-N-checklist.md` file. Skill-dir `general-checklist.md` / `phase-2-checklist.md` are templates only — use them as content sources, never copy them into the project as standalone files.
- **Gate Rule is mandatory.** See "Gate Rule (Mandatory)" section above. Never present work as complete while Gate is BLOCKED.
