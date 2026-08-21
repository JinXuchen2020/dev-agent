using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API request payload for creating a new agent.
/// </summary>
/// <param name="Name">The display name of the agent. Required.</param>
/// <param name="RoleCode">The role code to assign to the agent.</param>
/// <param name="ModelProvider">The model provider to use. When null, a configured default is applied.</param>
/// <param name="ModelName">The specific model name to use. When null, a configured default is applied.</param>
/// <param name="ModelApiUrl">The API endpoint URL for the model. When null, a configured default is applied.</param>
/// <param name="SystemPrompt">The system prompt that shapes the agent's behavior. When null, a configured default is applied.</param>
/// <param name="AllowedToolNames">Optional allow-list of tool names the agentic loop may invoke. Null = none permitted.</param>
/// <param name="MaxIterations">Optional upper bound on ReAct iterations. Null = default (25).</param>
/// <param name="StopCriteria">Optional natural-language stop condition for the agentic loop.</param>
public record CreateAgentRequest(
    [Required] string Name,
    string? RoleCode,
    string? ModelProvider,
    string? ModelName,
    string? ModelApiUrl,
    string? SystemPrompt,
    List<string>? AllowedToolNames,
    int? MaxIterations,
    string? StopCriteria);
