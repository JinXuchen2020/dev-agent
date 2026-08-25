using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a critic/review node within the negotiation preset (Blueprint C.6).
/// Reviews the previous node's artifact via <see cref="IModelRouter"/> and returns a structured
/// diff with either approval or rework instructions.
///
/// This enables range-specific rework (targeted fixes) instead of full pipeline restart.
/// F31 ②: model calls route through the router (tenant BYO first, platform fallback) instead of
/// the hardcoded platform-only client, so「我的凭据」works for critic nodes too.
/// Uses real review — fallback to "always approve" only if AllowCriticOverride=true and model is unavailable.
/// </summary>
internal sealed class CriticStepExecutor : IStepExecutor
{
    private readonly ILogger<CriticStepExecutor> _logger;
    private readonly IModelRouter _modelRouter;
    private readonly StateMachineSettings _settings;

    /// <summary>Legacy glob fallback — matches critic step names.</summary>
    public string StepType => "*critic*";

    /// <summary>Handles Critic-type nodes explicitly.</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Critic;

    public CriticStepExecutor(
        ILogger<CriticStepExecutor> logger,
        IModelRouter modelRouter,
        IOptions<StateMachineSettings> settings)
    {
        _logger = logger;
        _modelRouter = modelRouter;
        _settings = settings.Value;
    }

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        _logger.LogInformation("Critic reviewing step {StepName} for workflow {WorkflowId}",
            step.Name, ctx.WorkflowId);

        try
        {
            var lastArtifact = ctx.Artifacts.Values
                .OrderByDescending(a => a.ProducedAt)
                .FirstOrDefault();

            if (lastArtifact == null)
            {
                _logger.LogWarning("No artifacts to review for critic step {StepName}", step.Name);
                return StepExecutionResult.Success("No artifacts to review");
            }

            var systemPrompt = "You are a quality reviewer on a software development team. " +
                "Analyze the following artifact produced by a team member and determine if it meets quality standards. " +
                "Respond with a JSON object containing:\n" +
                "- \"Approved\": true if the artifact is acceptable, false if it needs rework\n" +
                "- \"Feedback\": specific, actionable feedback about the artifact\n" +
                "- \"ReworkTarget\": the step that should be reworked (or null if approved)\n" +
                "- \"Diff\": specific issues found (or null if approved)";

            var userPrompt = $"Review this {lastArtifact.ContentType} artifact from step '{lastArtifact.StepName}':\n\n" +
                Truncate(lastArtifact.Content, 4000);

            var messages = new List<ChatMessage>
            {
                new(MessageRole.System, systemPrompt),
                new(MessageRole.User, userPrompt)
            };

            CriticReviewResult reviewResult;

            try
            {
                // F31 ②: route via ModelRouter (tenant BYO → platform fallback) instead of the
                // hardcoded DefaultModelId platform-only client. No PreferredModel — critic has
                // no agent binding, so the tenant/platform default ordering applies.
                var request = new RoutingRequest(ctx.TenantId, messages);
                var response = await _modelRouter.RouteAsync(request, ct);
                reviewResult = ParseReviewResult(response.Content, lastArtifact.StepName, _settings.AllowCriticOverride);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_settings.AllowCriticOverride)
                {
                    _logger.LogWarning(ex,
                        "Critic model unavailable for step {StepName}, AllowCriticOverride=true — approving",
                        step.Name);
                    reviewResult = new CriticReviewResult
                    {
                        Approved = true,
                        StepName = lastArtifact.StepName,
                        Feedback = "Auto-approved (critic model unavailable, AllowCriticOverride=true).",
                        ReworkTarget = null,
                        Diff = null
                    };
                }
                else
                {
                    _logger.LogError(ex,
                        "Critic model threw for step {StepName}, AllowCriticOverride=false — rejecting (fail-loud)",
                        step.Name);
                    reviewResult = new CriticReviewResult
                    {
                        Approved = false,
                        StepName = lastArtifact.StepName,
                        Feedback = $"Rejected (critic model error): {ex.Message}",
                        ReworkTarget = lastArtifact.StepName,
                        Diff = $"Critic model threw: {ex.Message}"
                    };
                }
            }

            var artifactJson = JsonSerializer.Serialize(reviewResult);

            _logger.LogInformation("Critic review for {TargetStep}: {Verdict}",
                lastArtifact.StepName, reviewResult.Approved ? "APPROVED" : "REWORK_REQUIRED");

            return StepExecutionResult.Success(artifactJson, artifactJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critic step {StepName} failed", step.Name);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private static CriticReviewResult ParseReviewResult(string content, string fallbackStepName, bool allowOverride)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CriticReviewResult>(content);
            if (result != null)
            {
                if (string.IsNullOrEmpty(result.StepName))
                    result = result with { StepName = fallbackStepName };
                return result;
            }
        }
        catch (JsonException)
        {
        }

        if (allowOverride)
        {
            return new CriticReviewResult
            {
                Approved = true,
                StepName = fallbackStepName,
                Feedback = content.Length > 500 ? content[..500] : content,
                ReworkTarget = null,
                Diff = null
            };
        }

        return new CriticReviewResult
        {
            Approved = false,
            StepName = fallbackStepName,
            Feedback = $"Invalid critic response (unparseable JSON): {content[..Math.Min(content.Length, 200)]}",
            ReworkTarget = fallbackStepName,
            Diff = "Critic response was not valid JSON"
        };
    }

    private static string Truncate(string value, int maxLength) =>
        StringHelpers.Truncate(value, maxLength);
}

/// <summary>
/// Structured output from a critic/review step.
/// Enables range-specific rework: the critic specifies which step to rework and what to fix.
/// </summary>
internal sealed record CriticReviewResult
{
    public bool Approved { get; init; }
    public string StepName { get; init; } = "";
    public string Feedback { get; init; } = "";
    public string? ReworkTarget { get; init; }
    public string? Diff { get; init; }
}
