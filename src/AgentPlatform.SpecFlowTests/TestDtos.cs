using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// HTTP 响应 DTO（camelCase 与 API 一致；枚举按整数序列化，因 API 未注册 JsonStringEnumConverter）。
/// 仅声明 BDD 断言所需的字段；缺失字段由 System.Text.Json 忽略。
/// </summary>
public sealed record AgentRoleSummaryDto(
    Guid Id,
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt,
    bool IsBuiltIn,
    int AgentCount);

public sealed record AgentRoleResponseDto(
    Guid Id,
    string Name,
    string RoleCode,
    string Description,
    string SystemPrompt,
    bool IsBuiltIn);

public sealed record AgentResponseDto(
    Guid Id,
    string Name,
    string RoleCode,
    string? ModelProvider,
    string? ModelName,
    Guid TenantId,
    string Status,
    string SystemPrompt,
    DateTime CreatedAt);

public sealed record ExecutionLogSummaryDto(
    Guid Id,
    Guid WorkflowId,
    string WorkflowName,
    int Status,
    int TotalSteps,
    int CompletedSteps,
    int FailedSteps,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record ExecutionLogListResponseDto(
    IReadOnlyList<ExecutionLogSummaryDto> Items,
    int TotalCount);

public sealed record ExecutionLogStepEntryDto(
    Guid Id,
    string StepName,
    int StepOrder,
    int Status,
    TimeSpan Duration,
    string? Result,
    string? ErrorDetail,
    DateTime StartedAt,
    DateTime CompletedAt);

public sealed record ExecutionLogStepsResponseDto(
    IReadOnlyList<ExecutionLogStepEntryDto> Items,
    int TotalCount);

/// <summary>F12 工作流运行响应 DTO（camelCase；枚举按整数序列化）。仅声明断言所需字段。</summary>
public sealed record WorkflowNodeResponseDto(
    Guid Id,
    int Type,
    string Name,
    int State,
    string? Result,
    string? ErrorDetail);

/// <summary>F12 工作流详情 DTO（camelCase）。断言聚焦于 Nodes（图节点）的状态与真实输出回填。</summary>
public sealed record WorkflowDetailResponseDto(
    Guid Id,
    string Name,
    int CurrentState,
    IReadOnlyList<WorkflowNodeResponseDto>? Nodes,
    IReadOnlyList<object>? Edges);
