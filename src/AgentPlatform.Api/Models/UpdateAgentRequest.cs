using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API request payload for updating an existing agent.
/// All fields are optional; only the supplied fields are applied to the agent.
/// </summary>
/// <param name="Name">The new display name of the agent.</param>
/// <param name="RoleCode">The new role code to assign to the agent.</param>
/// <param name="ModelProvider">The new model provider.</param>
/// <param name="ModelName">The new model name.</param>
/// <param name="ModelApiUrl">The new API endpoint URL for the model.</param>
/// <param name="SystemPrompt">The new system prompt.</param>
/// <param name="Status">The new operational status (e.g. "Active", "Inactive").</param>
/// <param name="AllowedToolNames">Optional new allow-list of tool names for the agentic loop. Null = unchanged.</param>
/// <param name="MaxIterations">Optional new upper bound on ReAct iterations. Null = unchanged.</param>
/// <param name="StopCriteria">Optional new natural-language stop condition. Null = unchanged.</param>
public record UpdateAgentRequest(
    string? Name,
    string? RoleCode,
    string? ModelProvider,
    string? ModelName,
    string? ModelApiUrl,
    string? SystemPrompt,
    string? Status,
    List<string>? AllowedToolNames,
    int? MaxIterations,
    string? StopCriteria);
