using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Evaluation;

/// <summary>Response DTO for a single evaluation case.</summary>
public sealed record EvaluationCaseResponse(
    Guid Id,
    string Input,
    string ExpectedOutput,
    EvaluationMatchMode MatchMode);

/// <summary>Response DTO for a dataset in list view (no case bodies).</summary>
public sealed record EvaluationDatasetSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    int CaseCount,
    DateTime CreatedAt);

/// <summary>Response DTO for a dataset detail (includes cases).</summary>
public sealed record EvaluationDatasetDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<EvaluationCaseResponse> Cases,
    DateTime CreatedAt);

/// <summary>Internal input shape for a case supplied to create/update commands.</summary>
public sealed record CreateEvaluationCaseDto(
    string Input,
    string ExpectedOutput,
    EvaluationMatchMode MatchMode);

/// <summary>Per-case result inside an <see cref="EvaluationReport"/>.</summary>
public sealed record EvaluationCaseResult(
    string Input,
    string ExpectedOutput,
    string? ActualOutput,
    bool Passed,
    long DurationMs,
    int TokensIn,
    int TokensOut,
    string? ErrorDetail);

/// <summary>Aggregate report returned by a dataset evaluation run.</summary>
public sealed record EvaluationReport(
    int Total,
    int Passed,
    double Score,
    IReadOnlyList<EvaluationCaseResult> Cases);
