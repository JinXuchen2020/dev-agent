using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Agents;

/// <summary>
/// Orchestrates multi-agent collaboration using a sequential pipeline of six specialized agent roles:
/// Product Manager, Architect, Developer, Tester, Tech Writer, and Reviewer.
/// Each agent calls the LLM via <see cref="IModelClient"/> in sequence, building on prior responses.
/// The Reviewer determines whether the pipeline output is approved or needs rework.
/// </summary>
[Obsolete("Replaced by OrchestrationPrimitive with OrchestrationPreset.Negotiation (Blueprint C.2). " +
    "This class does NOT use AutoGen.NET symbols — it is a manual IModelClient loop. " +
    "Scheduled for removal in Phase 3 cleanup.")]
internal sealed class AutoGenAgentOrchestrator : IAgentOrchestrator
{
    private readonly ILogger<AutoGenAgentOrchestrator> _logger;
    private readonly AutoGenSettings _settings;
    private readonly IModelClient _modelClient;

    public AutoGenAgentOrchestrator(
        ILogger<AutoGenAgentOrchestrator> logger,
        IOptions<AutoGenSettings> settings,
        IModelClient modelClient)
    {
        _logger = logger;
        _settings = settings.Value;
        _modelClient = modelClient;
    }

    public async Task<string> RunCollaborationAsync(string input, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Starting multi-agent collaboration. Input: {Input}, MaxRounds: {MaxRounds}",
            input, _settings.MaxRounds);

        var roles = CreateRoleDefinitions();
        var conversationHistory = new List<string> { $"## User Request\n{input}\n" };

        var roundsUsed = 0;
        var maxRounds = Math.Min(_settings.MaxRounds, 20); // cap for safety

        for (var round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            roundsUsed++;

            foreach (var role in roles)
            {
                ct.ThrowIfCancellationRequested();

                var prompt = BuildPrompt(role.SystemMessage, conversationHistory);
                _logger.LogDebug("[{Agent}] Generating reply (round {Round})...", role.Name, round + 1);

                try
                {
                    var chatMessages = new List<ChatMessage>
                    {
                        new ChatMessage(Domain.Enums.MessageRole.System, role.SystemMessage),
                        new ChatMessage(Domain.Enums.MessageRole.User, prompt)
                    };

                    var response = await _modelClient.ChatAsync(_settings.DefaultModelId, chatMessages, ct);
                    var reply = response.Content;

                    _logger.LogDebug("[{Agent}] Reply generated ({Length} chars)", role.Name, reply.Length);

                    conversationHistory.Add($"=== {role.Name} ===\n{reply}\n");

                    // Check termination conditions
                    if (role.Name == "Reviewer" && reply.Contains("APPROVED", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Pipeline approved by Reviewer after {Round} rounds", round + 1);
                        return BuildOutput(conversationHistory, roundsUsed, true);
                    }

                    if (reply.Contains("REQUIREMENTS_READY", StringComparison.OrdinalIgnoreCase) ||
                        reply.Contains("ARCHITECTURE_READY", StringComparison.OrdinalIgnoreCase) ||
                        reply.Contains("CODE_READY", StringComparison.OrdinalIgnoreCase) ||
                        reply.Contains("TESTING_DONE", StringComparison.OrdinalIgnoreCase) ||
                        reply.Contains("DOCS_READY", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("[{Agent}] Handoff signal detected, continuing pipeline", role.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Agent}] Failed to generate reply", role.Name);
                    conversationHistory.Add($"=== {role.Name} ===\n[Error: {ex.Message}]\n");
                }
            }

            // After a full pipeline pass, if Reviewer didn't approve, loop back for rework
            _logger.LogInformation("Pipeline round {Round} completed without approval, looping for rework", round + 1);
        }

        _logger.LogWarning("Pipeline reached max rounds ({Max}) without approval", maxRounds);
        return BuildOutput(conversationHistory, roundsUsed, false);
    }

    /// <summary>
    /// Defines the six agent roles with their system messages.
    /// </summary>
    private static IReadOnlyList<RoleDefinition> CreateRoleDefinitions()
    {
        return new List<RoleDefinition>
        {
            new("ProductManager", """
                You are a Product Manager. Your role is to:
                - Clarify and refine the user's requirements
                - Break down the request into actionable user stories
                - Define acceptance criteria
                - Prioritize features and tasks
                Keep your responses concise and focused on requirements.
                When done, say "REQUIREMENTS_READY" to pass to the Architect.
                """),
            new("Architect", """
                You are a Software Architect. Your role is to:
                - Design the system architecture based on requirements
                - Define component boundaries and interfaces
                - Choose technology stacks and patterns
                - Produce an architecture design document
                Keep your responses technical and specific.
                When done, say "ARCHITECTURE_READY" to pass to the Developer.
                """),
            new("Developer", """
                You are a Senior Developer. Your role is to:
                - Implement the architecture design in working code
                - Write clean, well-structured, tested code
                - Follow best practices and design patterns
                - Produce the actual implementation
                Keep your responses focused on code and technical details.
                When done, say "CODE_READY" to pass to the Tester.
                """),
            new("Tester", """
                You are a QA Engineer. Your role is to:
                - Review the implemented code for defects
                - Write unit tests and integration tests
                - Verify edge cases and error handling
                - Report any bugs found
                Keep your responses focused on test coverage and quality.
                When done, say "TESTING_DONE" to pass to the Tech Writer.
                """),
            new("TechWriter", """
                You are a Technical Writer. Your role is to:
                - Document the API, architecture, and usage
                - Write clear, user-friendly documentation
                - Include code examples and setup instructions
                - Produce final documentation output
                Keep your responses clear and well-structured.
                When done, say "DOCS_READY" to pass to the Reviewer.
                """),
            new("Reviewer", """
                You are a Code Reviewer. Your role is to:
                - Review all deliverables: architecture, code, tests, docs
                - Verify consistency across all artifacts
                - Check for security issues and best practices
                - Provide final sign-off or request changes
                Keep your responses thorough and constructive.
                When all deliverables meet quality standards, say "APPROVED" to complete the pipeline.
                If changes are needed, state what needs to be fixed so the Developer can address them.
                """)
        };
    }

    /// <summary>
    /// Builds the user prompt for an agent, including the conversation history from all previous agents.
    /// The agent's system message is sent separately via a <c>System</c> role chat message.
    /// </summary>
    private static string BuildPrompt(string systemMessage, IReadOnlyList<string> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Previous Work");
        foreach (var entry in history)
        {
            sb.AppendLine(entry);
        }
        sb.AppendLine("---");
        sb.AppendLine("Now provide your contribution based on your role and the work done so far.");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the final output from the conversation history.
    /// </summary>
    private static string BuildOutput(IReadOnlyList<string> history, int roundsUsed, bool approved)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Multi-Agent Pipeline Output");
        sb.AppendLine($"**Status**: {(approved ? "Approved" : "Max rounds reached without approval")}");
        sb.AppendLine($"**Rounds used**: {roundsUsed}");
        sb.AppendLine();
        foreach (var entry in history)
        {
            sb.AppendLine(entry);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Defines an agent role with its name and system message.
    /// </summary>
    private sealed record RoleDefinition(string Name, string SystemMessage);
}
