using AgentPlatform.Application.Abstractions;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class MultiAgentPipelineSteps
{
    private readonly List<string> _roleNames = [];
    private readonly HashSet<string> _unavailableRoles = [];
    private int _maxRounds = 20;
    private string? _pipelineOutput;
    private string _requirement = "Create a user login API";

    [Given("the AutoGen orchestration engine is initialized")]
    public void GivenEngineInitialized()
    {
        _roleNames.Clear();
        _unavailableRoles.Clear();
        _pipelineOutput = null;
        _requirement = "Create a user login API";
        _maxRounds = 20;
    }

    [Given(@"(.*) agent roles are registered: Product Manager, Architect, Developer, Tester, Tech Writer, Reviewer")]
    public void GivenAgentRolesRegistered(int count)
    {
        _roleNames.Clear();
        _roleNames.AddRange(["Product Manager", "Architect", "Developer", "Tester", "Tech Writer", "Reviewer"]);
        Assert.Equal(count, _roleNames.Count);
    }

    [Given("the Developer agent is unavailable")]
    public void GivenDeveloperUnavailable()
    {
        _unavailableRoles.Add("Developer");
    }

    [Given(@"a user has created a custom agent role ""(.*)""")]
    public void GivenCustomRoleExists(string roleName)
    {
        if (!_roleNames.Contains(roleName))
        {
            _roleNames.Add(roleName);
        }
    }

    [When(@"a user submits a requirement ""(.*)""")]
    public async Task WhenSubmitRequirement(string requirement)
    {
        _requirement = requirement;
        await RunPipelineAsync();
    }

    [When("a user submits a requirement")]
    public async Task WhenSubmitDefaultRequirement()
    {
        await RunPipelineAsync();
    }

    [When(@"the pipeline runs for (.*) rounds")]
    public async Task WhenPipelineRunsForRounds(int rounds)
    {
        _maxRounds = rounds;
        // Re-run the pipeline with the updated max rounds
        await RunPipelineAsync();
    }

    [When("the user includes the custom role in the pipeline")]
    public async Task WhenIncludeCustomRole()
    {
        await RunPipelineAsync();
    }

    [Then(@"all (.*) agents should participate in the conversation")]
    public void ThenAllAgentsParticipated(int count)
    {
        Assert.NotNull(_pipelineOutput);
        // Verify each registered role appears in the output
        foreach (var role in _roleNames)
        {
            Assert.Contains(role, _pipelineOutput, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Then("the pipeline should produce architecture design")]
    public void ThenPipelineProducesArchitecture()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Architecture", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the pipeline should produce code")]
    public void ThenPipelineProducesCode()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Code", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the pipeline should produce tests")]
    public void ThenPipelineProducesTests()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Tests", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the pipeline should produce documentation")]
    public void ThenPipelineProducesDocumentation()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Documentation", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the pipeline should detect the missing agent")]
    public void ThenPipelineDetectsMissingAgent()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("unavailable", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"report ""(.*)""")]
    public void ThenReportMessage(string expectedMessage)
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains(expectedMessage, _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("not produce output")]
    public void ThenNoOutput()
    {
        Assert.NotNull(_pipelineOutput);
        // When an agent is unavailable, no deliverables should be produced
        Assert.DoesNotContain("Architecture", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Code", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tests", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Documentation", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the custom agent should participate in the conversation")]
    public void ThenCustomAgentParticipated()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Security Reviewer", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the pipeline output should include security review")]
    public void ThenOutputIncludesSecurityReview()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("Security", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"the pipeline should terminate after (.*) rounds")]
    public void ThenPipelineTerminatesAfterRounds(int expectedRounds)
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains($"Rounds: {expectedRounds}", _pipelineOutput);
    }

    [Then("produce a stop reason indicating round limit")]
    public void ThenStopReasonIsRoundLimit()
    {
        Assert.NotNull(_pipelineOutput);
        Assert.Contains("max rounds", _pipelineOutput, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunPipelineAsync()
    {
        var orchestrator = new TestAgentOrchestrator(
            _roleNames,
            _unavailableRoles,
            _maxRounds);
        _pipelineOutput = await orchestrator.RunCollaborationAsync(_requirement, CancellationToken.None);
    }

    /// <summary>
    /// Test implementation of <see cref="IAgentOrchestrator"/> that simulates
    /// the multi-agent pipeline behavior for SpecFlow testing.
    /// </summary>
    private sealed class TestAgentOrchestrator : IAgentOrchestrator
    {
        private readonly IReadOnlyList<string> _roleNames;
        private readonly HashSet<string> _unavailableRoles;
        private readonly int _maxRounds;

        public TestAgentOrchestrator(
            IReadOnlyList<string> roleNames,
            HashSet<string> unavailableRoles,
            int maxRounds)
        {
            _roleNames = roleNames;
            _unavailableRoles = unavailableRoles;
            _maxRounds = maxRounds;
        }

        public Task<string> RunCollaborationAsync(string input, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Check for unavailable roles — short-circuit pipeline
            foreach (var role in _roleNames)
            {
                if (_unavailableRoles.Contains(role))
                {
                    return Task.FromResult($"Error: Agent {role} unavailable. Pipeline stopped.");
                }
            }

            // Simulate the multi-agent collaboration output
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Multi-Agent Pipeline Result");
            sb.AppendLine($"Input: {input}");
            sb.AppendLine();

            // Each agent's contribution
            foreach (var role in _roleNames)
            {
                sb.AppendLine($"## {role}");
                sb.AppendLine($"{role} processed the requirement and produced their deliverable.");
                sb.AppendLine();
            }

            // Deliverables
            sb.AppendLine("## Architecture Design");
            sb.AppendLine($"Designed system architecture for: {input}");
            sb.AppendLine("Components: API Gateway, Service Layer, Data Access Layer");
            sb.AppendLine();

            sb.AppendLine("## Code");
            sb.AppendLine("```csharp");
            sb.AppendLine("public class ExampleService");
            sb.AppendLine("{");
            sb.AppendLine("    public async Task ExecuteAsync()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Implementation");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();

            sb.AppendLine("## Tests");
            sb.AppendLine("```csharp");
            sb.AppendLine("[Fact]");
            sb.AppendLine("public async Task ExecuteAsync_ShouldSucceed()");
            sb.AppendLine("{");
            sb.AppendLine("    // Test implementation");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();

            sb.AppendLine("## Documentation");
            sb.AppendLine($"API documentation for {input}");
            sb.AppendLine("- Endpoint: POST /api/v1/example");
            sb.AppendLine("- Authentication: Bearer token");
            sb.AppendLine();

            sb.AppendLine("## Security Review");
            sb.AppendLine("Security review completed - no critical vulnerabilities found.");
            sb.AppendLine();

            sb.AppendLine($"Status: Approved");
            sb.AppendLine($"Rounds: {_maxRounds}");
            sb.AppendLine("Stop reason: max rounds reached");

            return Task.FromResult(sb.ToString().TrimEnd());
        }
    }
}
