using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API request payload for running an autonomous agentic agent against a goal.
/// </summary>
/// <param name="Goal">The objective the agent should accomplish via its ReAct control loop.</param>
public record RunAgentGoalRequest([Required] string Goal);
