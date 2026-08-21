# Audit Patterns

Grep/Glob patterns for codebase auditing. Run each pattern, read matching files to confirm, then classify by severity.

---

## 1. DI Registration Gaps

**Pattern:** Find interfaces in Application.Abstractions that are NOT registered in Infrastructure/DependencyInjection.cs.

```
Grep pattern: "interface I" in src/AgentPlatform.Application/Abstractions/
```

Then for each interface found, check if it appears in `src/AgentPlatform.Infrastructure/DependencyInjection.cs`:

```
Grep pattern: interface_name in src/AgentPlatform.Infrastructure/DependencyInjection.cs
```

**Severity:** P1 (service won't resolve at runtime)

**False positive check:** Some interfaces are resolved by factory methods or conditional registration. Read the DI file carefully.

---

## 2. DDD Layer Violations

### 2a. Implementation class in Application layer

```
Glob pattern: src/AgentPlatform.Application/**/*.cs
```

Look for classes that implement Infrastructure concerns (database access, external API calls, Redis). Application layer should only contain Command/Query handlers and Domain Services.

**Check:** Does any file in `Application/` reference `Infrastructure` namespace or types?

```
Grep pattern: "using AgentPlatform.Infrastructure" in src/AgentPlatform.Application/
```

**Severity:** P1 (DDD dependency direction violation)

### 2b. Interface defined in Infrastructure

```
Grep pattern: "public interface I" in src/AgentPlatform.Infrastructure/
```

Interfaces should be in Application.Abstractions or Domain.Abstractions, not Infrastructure.

**Severity:** P1 (DDD layer violation)

### 2c. Domain project external dependencies

```
Read file: src/AgentPlatform.Domain/AgentPlatform.Domain.csproj
```

Check for any `<PackageReference>` elements. Domain should have zero external packages.

**Severity:** P0 (DDD core violation — Domain must be dependency-free)

---

## 3. EF Core Mapping Gaps

**Pattern:** Find aggregate roots that lack IEntityTypeConfiguration.

```
Grep pattern: "class.*IAggregateRoot" in src/AgentPlatform.Domain/
```

For each aggregate root found, check if a configuration class exists:

```
Grep pattern: "IEntityTypeConfiguration<AggregateRootName>" in src/AgentPlatform.Infrastructure/
```

Also check for value objects without `OwnsOne`:

```
Grep pattern: "record struct" in src/AgentPlatform.Domain/
```

For each value object, verify it is mapped via `OwnsOne` or `OwnsMany` in an EF Core configuration.

**Severity:** P1 (runtime EF Core exception on SaveChanges)

---

## 4. Hardcoded Values

### 4a. Hardcoded GUIDs

```
Grep pattern: "Guid.Parse\(" in src/
Grep pattern: "new Guid\(" in src/
```

Exclude test files. Any hardcoded GUID in production code should be configuration-driven.

**Severity:** P2 (not configurable)

### 4b. Hardcoded numbers in business logic

```
Grep pattern: "retry.*=.*[0-9]" in src/
Grep pattern: "timeout.*=.*[0-9]" in src/
Grep pattern: "budget.*=.*[0-9]" in src/
```

Exclude configuration classes and test files. Magic numbers in business logic should use IOptions.

**Severity:** P2

### 4c. Hardcoded model names

```
Grep pattern: "\"gpt-" in src/
Grep pattern: "\"claude-" in src/
Grep pattern: "\"deepseek" in src/
```

Exclude appsettings files. Model names should come from configuration.

**Severity:** P2

---

## 5. Missing CancellationToken

**Pattern:** async methods without CancellationToken parameter.

```
Grep pattern: "async.*Task.*\(\)" in src/
Grep pattern: "async.*Task<.*>\(\)" in src/
```

Methods returning Task without parameters likely miss CancellationToken. Also check:

```
Grep pattern: "next\(\)" in src/
```

In MediatR pipeline behaviors, `next()` without cancellationToken is a bug (should be `next(cancellationToken)`).

**Severity:** P1 (cancellation not propagated, tests may hang)

---

## 6. Missing Modifiers

### 6a. Implementation classes without sealed

```
Grep pattern: "internal class " in src/AgentPlatform.Infrastructure/
Grep pattern: "public class " in src/AgentPlatform.Application/
```

Should be `internal sealed class` and `public sealed class` respectively. Unsealed implementation classes are a code quality issue.

**Severity:** P3

### 6b. Missing null guards on public methods

```
Grep pattern: "public.*\(" in src/AgentPlatform.Application/
```

For each public method, check if parameters have `ArgumentNullException.ThrowIfNull` or similar guard. String parameters should have `ArgumentException.ThrowIfNullOrWhiteSpace`.

**Severity:** P2

---

## 7. Concurrency Risks

### 7a. Singleton with mutable state

```
Grep pattern: "AddSingleton" in src/AgentPlatform.Infrastructure/DependencyInjection.cs
```

For each Singleton, read the implementation class and check for mutable fields (non-readonly, non-const instance fields). Mutable state in Singleton without `lock` or `ConcurrentXxx` is a concurrency bug.

**Severity:** P1 (race condition)

### 7b. Dictionary without Concurrent

```
Grep pattern: "Dictionary<" in src/AgentPlatform.Infrastructure/
Grep pattern: "Dictionary<" in src/AgentPlatform.Application/
```

`Dictionary<TKey, TValue>` in services that may be accessed concurrently should be `ConcurrentDictionary`. Check the DI lifetime of the containing class.

**Severity:** P1 if Singleton/Singleton-like, P2 if Scoped

### 7c. decimal accumulator without lock

```
Grep pattern: "\\+= .*[Dd]ecimal" in src/
Grep pattern: "\\+= .*_spent" in src/
Grep pattern: "\\+= .*_total" in src/
```

`decimal` does not support `Interlocked`. Any `+=` on a decimal field in a shared service needs `lock`.

**Severity:** P1

---

## 8. Missing Null Guards

```
Grep pattern: "public.*string " in src/AgentPlatform.Domain/
```

Domain entity and value object constructor/method parameters should have null/empty guards.

Also check:

```
Grep pattern: "null!" in src/AgentPlatform.Domain/
```

`null!` suppressions should have a comment explaining why (usually EF Core proxy creation).

**Severity:** P2

---

## 9. API Infrastructure

### 9a. Missing exception handling

```
Grep pattern: "UseExceptionHandler" in src/AgentPlatform.Api/Program.cs
Grep pattern: "ProblemDetails" in src/AgentPlatform.Api/Program.cs
```

If not found, API has no global exception handling.

**Severity:** P1

### 9b. Missing CORS

```
Grep pattern: "AddCors" in src/AgentPlatform.Api/Program.cs
Grep pattern: "UseCors" in src/AgentPlatform.Api/Program.cs
```

**Severity:** P2 (frontend cannot call API)

### 9c. Missing Health Checks

```
Grep pattern: "MapHealthChecks" in src/AgentPlatform.Api/Program.cs
```

**Severity:** P3

### 9d. Controller direct service access (bypassing MediatR)

```
Grep pattern: "class.*Controller" in src/AgentPlatform.Api/Controllers/
```

For each controller, check if it injects Application layer services directly (should inject `IMediator` only).

```
Grep pattern: "I.*Service" in src/AgentPlatform.Api/Controllers/
```

If a controller injects anything other than `IMediator` and `IOptions<T>`, it likely bypasses MediatR.

**Severity:** P1 (DDD violation, no pipeline behavior)

---

## 10. Blueprint Drift

Check if blueprint describes features that are not implemented in code.

```
Read file: AGENT_PLATFORM_BLUEPRINT.md
```

For each feature described, search for implementation:

```
Grep pattern: feature_keyword in src/
```

Common drift items to check:
- JWT/Identity: search for `JwtBearer` or `Identity` in Program.cs
- OpenTelemetry: search for `OpenTelemetry` in Program.cs or .csproj
- CI/CD: search for `.github/workflows/` or `azure-pipelines.yml`
- Multi-tenant dynamic filter: search for `ITenantProvider` implementation

**Behavioral-usage rule (critical — fixes false "completed"):** A keyword match counts as "implemented" ONLY when it lands on a **behavioral use site** — a method body that *emits / invokes / reads* the symbol, an injected dependency, or a real call site. A match that lands ONLY on a **declaration** — an enum member, a declared constant, an interface/abstract type, or an empty stub class with no body — is NOT evidence of implementation. Such "defined-but-never-used" symbols are themselves dead code and produce a false "completed" signal; treat the feature as NOT implemented and continue to the drift check below. (Concrete case: `AuditActionType.KeyRotation` is defined in the enum but zero code emits it → the key-rotation audit feature is unimplemented, not done.)

If blueprint describes it but code doesn't have a behavioral use site, check if blueprint marks it as future phase. If not marked, it's drift.

**Severity:** P1 (false "completed" — launch-blocking gap; do NOT downgrade to P2/P3)

---

## 11. Missing XML Documentation Comments

**Pattern:** Find public types and members that lack `/// <summary>` XML documentation.

### 11a. Public types without XML comment

```
Grep pattern: "^\s*public\s+(sealed\s+)?(class|record|interface|enum|struct)\s" in src/
```

For each match, check the preceding 1-3 lines for `///` comments. If not found, it's a finding.

Exclude: test files, generated files (*.feature.cs), DTO request/response models that are self-documenting.

### 11b. Public methods without XML comment

```
Grep pattern: "^\s*public\s+(async\s+)?(Task|void|bool|string|Money|IActionResult|IReadOnlyList)" in src/
```

For each match, check the preceding 1-3 lines for `///` comments. Constructor methods can be excluded.

### 11c. Public interface members without XML comment

```
Grep pattern: "^\s*(Task|bool|void|Money|string|IReadOnlyList|int|decimal)\s+\w+\(" in src/AgentPlatform.Application/Abstractions/
```

For each match in interface files, check the preceding lines for `///` comments. Every interface method should have `<summary>` and `<param>` tags.

### 11d. Public properties on settings/config classes

```
Grep pattern: "public\s+\w+\s+\w+\s*\{.*get" in src/AgentPlatform.Application/Abstractions/
```

For each match, check if the property has a `///` comment. Settings properties should document their purpose and valid values.

**Severity:** P3 (code quality — missing IntelliSense documentation)

**False positive check:** Auto-implemented properties with self-documenting names on simple DTOs can be skipped. Properties with non-obvious behavior, side effects, or business meaning must have comments.

---

## 12. Swagger / API Documentation

### 12a. Missing Swashbuckle or OpenAPI package

```
Grep pattern: "Swashbuckle" in src/AgentPlatform.Api/AgentPlatform.Api.csproj
Grep pattern: "OpenApi" in src/AgentPlatform.Api/AgentPlatform.Api.csproj
```

At least one API documentation package must be referenced. If neither Swashbuckle.AspNetCore nor Microsoft.AspNetCore.OpenApi is present, API has no documentation endpoint.

**Severity:** P2 (no API documentation available for consumers)

### 12b. Missing SwaggerGen or AddOpenApi configuration

```
Grep pattern: "AddSwaggerGen" in src/AgentPlatform.Api/Program.cs
Grep pattern: "AddOpenApi" in src/AgentPlatform.Api/Program.cs
```

At least one must be present in the service registration section. If neither is found, API documentation is not configured.

**Severity:** P2

### 12c. Missing Swagger UI or Scalar endpoint

```
Grep pattern: "UseSwaggerUI" in src/AgentPlatform.Api/Program.cs
Grep pattern: "MapScalarApiReference" in src/AgentPlatform.Api/Program.cs
```

At least one UI endpoint must be present in the Development environment block. If neither is found, there is no browsable API documentation UI.

**Severity:** P2

### 12d. Missing XML documentation file generation

```
Grep pattern: "<GenerateDocumentationFile>true</GenerateDocumentationFile>" in src/AgentPlatform.Api/AgentPlatform.Api.csproj
```

If not found, XML doc comments will not be generated into a file, and Swagger cannot display them.

Also check Application project:

```
Grep pattern: "<GenerateDocumentationFile>true</GenerateDocumentationFile>" in src/AgentPlatform.Application/AgentPlatform.Application.csproj
```

Application layer generates DTO/command/query XML docs that Swagger should display.

**Severity:** P2 (Swagger shows no descriptions for types and members)

### 12e. Missing IncludeXmlComments in Swagger config

```
Grep pattern: "IncludeXmlComments" in src/AgentPlatform.Api/Program.cs
```

If Swashbuckle is used, `IncludeXmlComments` must be called for at least the Api assembly XML file. If missing, XML doc comments are generated but not shown in Swagger.

**Severity:** P2

### 12f. Controller actions without XML doc

```
Grep pattern: "\\[Http(Get|Post|Put|Delete|Patch)" in src/AgentPlatform.Api/Controllers/
```

For each match, check the preceding 1-3 lines for `///` comments. Every API action method should have `/// <summary>` for Swagger to display meaningful descriptions.

**Severity:** P3 (Swagger shows empty descriptions for endpoints)

**False positive check:** If the controller class has a `/// <summary>` and the method name is self-documenting, the method can be skipped only if it has no parameters. Methods with parameters should always have `<param>` tags for Swagger parameter descriptions.

---

## 13. Architecture Tests (Compile-time DDD Constraints)

### 13a. Architecture test project exists

```
Glob pattern: src/AgentPlatform.ArchitectureTests/*.csproj
```

Verify the test project exists. If missing, architecture constraints are not enforced by CI.

**Severity:** P2 (first line of defense for DDD dependency rules is missing)

### 13b. Architecture test project covers all layers

```
Read file: src/AgentPlatform.ArchitectureTests/DddLayerTests.cs
```

Check for test methods covering at least:
- Domain has zero PackageReference (P0 if missing)
- Application does not reference Infrastructure (P1 if missing)
- Aggregate roots have IEntityTypeConfiguration (P1 if missing)
- Controllers inject IMediator only (P1 if missing)
- Interfaces in Abstractions have DI registration (P1 if missing)
- Infrastructure impl classes are internal sealed (P3 if missing)

List which tests exist and which are missing.

**Severity:** Varies per missing test (see above)

### 13c. All architecture tests pass

```
Bash command: dotnet test src/AgentPlatform.ArchitectureTests --no-restore
```

If any test fails, the architecture constraints are violated.

**Severity:** P0 (blocker if any test fails)

---

## 14. Integration Tests

### 14a. Integration test project exists

```
Glob pattern: src/AgentPlatform.IntegrationTests/*.csproj
```

When Phase 2 introduces Redis and real PGVector, integration tests are required.

**Severity:** P2 when Phase 2 items are implemented without tests; N/A in Phase 1

### 14b. Testcontainers fixtures for external dependencies

```
Grep pattern: "Testcontainers." in src/AgentPlatform.IntegrationTests/
```

For each external dependency (PostgreSQL, Redis, etc.), check if a Testcontainers fixture exists:
- `PostgreSqlContainerFixture` for PostgreSQL/PGVector
- `RedisContainerFixture` for Redis/StackExchange

**Severity:** P2 (missing fixture means integration tests use mocks, not real infrastructure)

### 14c. Integration tests run only when Docker is available

```
Grep pattern: "if: false" in .github/workflows/ci.yml
```

Integration tests should be conditionally gated on Docker availability in CI. If missing, the workflow will fail on runners without Docker.

**Severity:** P3 (CI configuration)

---

## 15. Security Vulnerabilities

### 15a. Known vulnerable NuGet packages

```
Bash command: dotnet list src/AgentPlatform.sln package --vulnerable
```

Run this command and check output for "has known vulnerable" lines. Any vulnerable package with a known CVE must be addressed.

**Severity:** P0 (CVE with known exploit), P1 (CVE with no known active exploit)

### 15b. CI workflow checks for vulnerabilities

```
Grep pattern: "--vulnerable" in .github/workflows/ci.yml
```

Every PR should check for vulnerable packages. If the CI workflow does not include this check, new vulnerable dependencies can be merged silently.

**Severity:** P2 (missing automated vulnerability scanning)

### 15c. CI workflow exists and builds + tests

```
Glob pattern: .github/workflows/ci.yml
```

A CI workflow must:
- Build the solution (Release configuration)
- Run unit and architecture tests
- Run vulnerability check

If any of these is missing, CI is incomplete.

**Severity:** P2 (CI gap)

---

## 16. Chinese XML Comments Enforcement

**Pattern:** All new public types and public members MUST have Chinese XML comments (`/// <summary>` containing Chinese characters like 中文).

### 16a. Public types without Chinese XML comments

```
Grep pattern: "^\s*public\s+(sealed\s+)?(class|record|interface|enum|struct)\s" in src/
```

For each match, check the preceding 1-3 lines for `///` comments containing Chinese characters (e.g. `汉字`). If no Chinese XML comment is found, it's a finding.

Exclude: test files, generated files (*.feature.cs), auto-generated code.

**Severity:** P3

### 16b. Public methods without Chinese XML comments

```
Grep pattern: "^\s*public\s+(async\s+)?(Task|void|bool|string|int|decimal|Money|IActionResult|IReadOnlyList)\s+\w+\(" in src/
```

For each match, check the preceding 1-3 lines for `///` comments containing Chinese characters. Constructor methods and simple property getters can be excluded.

Exclude: test files, generated files, controller action methods (those are checked in 12f, but they SHOULD also have Chinese comments).

**Severity:** P3

### 16c. Interface members without Chinese XML comments

```
Grep pattern: "^\s*(Task|bool|void|string|int|decimal|Money|IReadOnlyList)\s+\w+\(" in src/AgentPlatform.Application/Abstractions/
```

For each match in interface files, check the preceding lines for `///` comments containing Chinese characters. Every interface method must have Chinese `<summary>` and `<param>` tags.

**Severity:** P3

### 16d. Verify existing XML comments contain Chinese

```
Grep pattern: "^/// <summary>" in src/
```

For each match, verify the content after `<summary>` contains Chinese characters. If only English, flag for update.

**Severity:** P3

**False positive check:** English-only XML comments are acceptable only when:
1. The member name is fully self-documenting and the comment adds no value (e.g., `Id` property → "获取唯一标识")
2. The comment is a pure cross-reference (`<see cref="..."/>`)
3. The member is in test code (excluded entirely)

**Note:** This category applies to **new code** added in the current phase. Pre-existing code without Chinese comments should be flagged but not block the phase.

---

## Running the Audit

1. Run all categories in parallel where possible (independent Grep calls).
2. For each Grep match, Read the file to confirm it's a real issue (not a false positive).
3. Collect all confirmed findings.
4. Classify by severity (P0 > P1 > P2 > P3).
5. Present as a table: `| Severity | Category | File | Finding | Suggested Fix |`
6. Count findings per category and per severity.
7. If any P0 or P1 findings exist, recommend immediate resolution before proceeding.
