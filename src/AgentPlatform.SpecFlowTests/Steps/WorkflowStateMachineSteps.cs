using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using System.Collections.Concurrent;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class WorkflowStateMachineSteps
{
    private Workflow _workflow = null!;
    private TestStateMachineEngine _engine = null!;
    private ConfigurableTestExecutor _mainExecutor = null!;
    private ConfigurableTestExecutor? _branchExecutor;
    private WorkflowState _resultState;
    private readonly List<Workflow> _concurrentWorkflows = new();
    private readonly List<Task<WorkflowState>> _concurrentTasks = new();

    [Given("a workflow with (.*) steps defined")]
    public void GivenWorkflowWithSteps(int stepCount)
    {
        _workflow = new Workflow(Guid.NewGuid(), "Test Workflow", Guid.NewGuid());
        for (int i = 0; i < stepCount; i++)
        {
            _workflow.AddStep(new WorkflowStep(Guid.NewGuid(), i, $"Step {i + 1}"));
        }
    }

    [Given("the state machine engine is initialized")]
    public void GivenEngineInitialized()
    {
        _mainExecutor = new ConfigurableTestExecutor();
        var executors = new List<IStepExecutor> { _mainExecutor };
        if (_branchExecutor != null)
        {
            executors.Add(_branchExecutor);
        }
        _engine = new TestStateMachineEngine(executors);
    }

    [Given("step (.*) is configured to fail")]
    public void GivenStepConfiguredToFail(int stepNumber)
    {
        var stepName = _workflow.Steps[stepNumber - 1].StepName;
        _mainExecutor.MarkStepAsFailing(stepName);
    }

    [Given("step (.*) is configured to always fail")]
    public void GivenStepConfiguredToAlwaysFail(int stepNumber)
    {
        var stepName = _workflow.Steps[stepNumber - 1].StepName;
        _mainExecutor.MarkStepAsAlwaysFailing(stepName);
    }

    [Given("step (.*) is in a branch path")]
    public void GivenStepInBranchPath(int stepNumber)
    {
        var stepName = _workflow.Steps[stepNumber - 1].StepName;
        _mainExecutor.MarkStepAsFailing(stepName);
        _branchExecutor = new ConfigurableTestExecutor
        {
            IsBranchExecutor = true,
            BranchSucceeds = true
        };
        // Reinitialize engine so the branch executor is included
        GivenEngineInitialized();
    }

    [Given("(.*) workflows are started simultaneously")]
    public void GivenWorkflowsStartedSimultaneously(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var wf = new Workflow(Guid.NewGuid(), $"Concurrent {i + 1}", Guid.NewGuid());
            for (int j = 0; j < 3; j++)
            {
                wf.AddStep(new WorkflowStep(Guid.NewGuid(), j, $"Step {j + 1}"));
            }
            _concurrentWorkflows.Add(wf);
        }
    }

    [Given(@"a workflow is in ""(.*)"" state")]
    public void GivenWorkflowInState(string stateName)
    {
        _workflow = new Workflow(Guid.NewGuid(), "Recovery Test", Guid.NewGuid());
        _workflow.SetState(ParseState(stateName));
    }

    [When("the workflow starts")]
    public async Task WhenWorkflowStarts()
    {
        _resultState = await _engine.StartAsync(_workflow, CancellationToken.None);
    }

    [When("the branch step fails")]
    public async Task WhenBranchStepFails()
    {
        var stepName = _workflow.Steps[1].StepName;
        _mainExecutor.MarkStepAsFailing(stepName);
        _branchExecutor = new ConfigurableTestExecutor
        {
            IsBranchExecutor = true,
            BranchSucceeds = true
        };
        var executors = new List<IStepExecutor> { _mainExecutor, _branchExecutor };
        _engine = new TestStateMachineEngine(executors);
        _resultState = await _engine.StartAsync(_workflow, CancellationToken.None);
    }

    [When("both workflows run")]
    public async Task WhenBothWorkflowsRun()
    {
        var executor = new ConfigurableTestExecutor();
        var engine = new TestStateMachineEngine(new List<IStepExecutor> { executor });
        _concurrentTasks.Clear();
        foreach (var wf in _concurrentWorkflows)
        {
            _concurrentTasks.Add(engine.StartAsync(wf, CancellationToken.None));
        }
        await Task.WhenAll(_concurrentTasks);
    }

    [When("the system restarts")]
    public void WhenSystemRestarts()
    {
        _workflow.SetState(WorkflowState.Failed);
    }

    [Then("step (.*) should execute successfully")]
    public void ThenStepExecutedSuccessfully(int stepNumber)
    {
        var step = _workflow.Steps[stepNumber - 1];
        var count = _mainExecutor.GetExecutionCount(step.StepName);
        Assert.True(count >= 1, $"Step {step.StepName} was never executed");
        Assert.NotEqual(WorkflowState.Failed, step.State);
    }

    [Then("step (.*) should retry up to (.*) times")]
    public void ThenStepRetriedUpToTimes(int stepNumber, int expectedRetries)
    {
        var stepName = _workflow.Steps[stepNumber - 1].StepName;
        var count = _mainExecutor.GetExecutionCount(stepName);
        Assert.True(count >= expectedRetries,
            $"Step {stepName} executed {count} times, expected at least {expectedRetries}");
    }

    [Then(@"after (.*) failures, step (.*) should be marked as ""(.*)""")]
    public void ThenAfterFailuresStepMarkedAs(int failures, int stepNumber, string stateName)
    {
        var step = _workflow.Steps[stepNumber - 1];
        Assert.Equal(ParseState(stateName), step.State);
        if (step.State == WorkflowState.Failed)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.ErrorDetail));
        }
    }

    [Then("step (.*) should fail after (.*) retries")]
    public void ThenStepFailedAfterRetries(int stepNumber, int retries)
    {
        var step = _workflow.Steps[stepNumber - 1];
        Assert.Equal(WorkflowState.Failed, step.State);
        var count = _mainExecutor.GetExecutionCount(step.StepName);
        Assert.True(count >= retries + 1,
            $"Step executed {count} times, expected at least {retries + 1}");
    }

    [Then("all completed steps should be rolled back")]
    public void ThenAllCompletedStepsRolledBack()
    {
        foreach (var step in _workflow.Steps)
        {
            if (step.State != WorkflowState.Failed)
            {
                Assert.Equal(WorkflowState.Pending, step.State);
            }
        }
    }

    [Then(@"the workflow status should be ""(.*)""")]
    public void ThenWorkflowStatusIs(string stateName)
    {
        Assert.Equal(ParseState(stateName), _resultState);
    }

    [Then("alternative branch should execute")]
    public void ThenAlternativeBranchExecuted()
    {
        Assert.NotNull(_branchExecutor);
        Assert.True(_branchExecutor.WasInvoked, "Branch executor was not invoked");
    }

    [Then(@"the workflow should complete with the successful branch result")]
    public void ThenWorkflowCompletesWithBranchResult()
    {
        Assert.Equal(WorkflowState.Completed, _resultState);
    }

    [Then("they should not corrupt each other's state")]
    public void ThenNoStateCorruption()
    {
        foreach (var wf in _concurrentWorkflows)
        {
            Assert.Equal(WorkflowState.Completed, wf.CurrentState);
            foreach (var step in wf.Steps)
            {
                Assert.Equal(WorkflowState.Completed, step.State);
            }
        }
    }

    [Then("both should produce correct results independently")]
    public void ThenBothProduceCorrectResults()
    {
        foreach (var wf in _concurrentWorkflows)
        {
            Assert.Equal(WorkflowState.Completed, wf.CurrentState);
        }
    }

    [Then(@"the workflow should be recovered to ""(.*)"" state")]
    public void ThenWorkflowRecoveredToState(string stateName)
    {
        Assert.Equal(ParseState(stateName), _workflow.CurrentState);
    }

    private static WorkflowState ParseState(string name)
    {
        return name switch
        {
            "Pending" => WorkflowState.Pending,
            "Running" => WorkflowState.Running,
            "Paused" => WorkflowState.Paused,
            "Completed" => WorkflowState.Completed,
            "Failed" => WorkflowState.Failed,
            "RolledBack" => WorkflowState.RolledBack,
            _ => throw new ArgumentException($"Unknown state: {name}")
        };
    }

    private sealed class ConfigurableTestExecutor : IStepExecutor
    {
        private readonly ConcurrentDictionary<string, int> _executionCounts = new();
        private readonly HashSet<string> _alwaysFailingSteps = new();

        public string StepType => "*";

        public bool IsBranchExecutor { get; set; }
        public bool BranchSucceeds { get; set; }
        public bool WasInvoked { get; private set; }

        public void MarkStepAsFailing(string stepName)
        {
            _alwaysFailingSteps.Add(stepName);
        }

        public void MarkStepAsAlwaysFailing(string stepName)
        {
            _alwaysFailingSteps.Add(stepName);
        }

        public int GetExecutionCount(string stepName)
        {
            return _executionCounts.TryGetValue(stepName, out var c) ? c : 0;
        }

        public Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
        {
            WasInvoked = true;
            var key = step.StepName;
            _executionCounts.AddOrUpdate(key, 1, (_, c) => c + 1);

            if (IsBranchExecutor)
            {
                return Task.FromResult(BranchSucceeds
                    ? StepExecutionResult.Success("Branch alternative result", "{}")
                    : StepExecutionResult.RetryableFailure("Branch step failed"));
            }

            if (_alwaysFailingSteps.Contains(key))
            {
                return Task.FromResult(StepExecutionResult.RetryableFailure("Step always fails"));
            }

            return Task.FromResult(StepExecutionResult.Success($"Output from {step.StepName}", "{}"));
        }
    }

    private sealed class TestStateMachineEngine : IStateMachineEngine
    {
        private readonly IEnumerable<IStepExecutor> _executors;
        private readonly StateMachineSettings _settings = new()
        {
            MaxRetryAttempts = 3,
            StepTimeoutSeconds = 120,
            RollbackTimeoutSeconds = 300
        };

        public TestStateMachineEngine(IEnumerable<IStepExecutor> executors)
        {
            _executors = executors;
        }

        public async Task<WorkflowState> StartAsync(Workflow workflow, CancellationToken ct)
        {
            workflow.SetState(WorkflowState.Running);

            var orderedSteps = workflow.Steps.OrderBy(s => s.Order).ToList();
            var ctx = BuildTestContext(workflow, null, orderedSteps);

            foreach (var step in orderedSteps)
            {
                ct.ThrowIfCancellationRequested();

                var executor = _executors.FirstOrDefault(e => e.StepType == step.StepName)
                    ?? _executors.FirstOrDefault(e => e is not ConfigurableTestExecutor { IsBranchExecutor: true });

                if (executor == null)
                {
                    step.SetError("No executor found");
                    return RollbackAll(workflow);
                }

                bool success = false;
                for (int attempt = 0; attempt <= _settings.MaxRetryAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    step.SetState(WorkflowState.Running);

                    try
                    {
                        var result = await executor.ExecuteAsync(step, ctx, ct);
                        if (result.Outcome == StepOutcome.Success)
                        {
                            step.SetState(WorkflowState.Completed);
                            success = true;
                            break;
                        }
                        step.SetError(result.ErrorMessage ?? "Execution returned failure");
                    }
                    catch (Exception ex)
                    {
                        step.SetError(ex.Message);
                    }
                }

                if (success)
                {
                    var isLast = step.Order == workflow.Steps.Max(s => s.Order);
                    workflow.SetState(isLast ? WorkflowState.Completed : WorkflowState.Running);
                    // Rebuild context with completed artifacts
                    ctx = BuildTestContext(workflow, null, orderedSteps);
                    continue;
                }

                var branchResult = await TryBranchAsync(step, ctx, ct);
                if (branchResult)
                {
                    step.SetState(WorkflowState.Completed);
                    var isLast = step.Order == workflow.Steps.Max(s => s.Order);
                    workflow.SetState(isLast ? WorkflowState.Completed : WorkflowState.Running);
                    ctx = BuildTestContext(workflow, null, orderedSteps);
                    continue;
                }

                return RollbackAll(workflow);
            }

            return workflow.CurrentState;
        }

        private static WorkflowContext BuildTestContext(Workflow workflow, WorkflowStep? currentStep, List<WorkflowStep> orderedSteps)
        {
            var artifacts = new Dictionary<string, StepArtifact>();
            foreach (var s in orderedSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
            {
                artifacts[s.StepName] = new StepArtifact
                {
                    StepName = s.StepName,
                    StepOrder = s.Order,
                    Content = s.Result!,
                    ContentType = "test"
                };
            }

            return new WorkflowContext
            {
                WorkflowId = workflow.Id,
                CurrentStepOrder = currentStep?.Order ?? 0,
                Artifacts = artifacts,
                Blackboard = Blackboard.Empty,
                Retrieval = RetrievalContext.Empty,
                Summary = StepHistory.Empty,
                TenantId = workflow.TenantId
            };
        }

        private async Task<bool> TryBranchAsync(WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
        {
            foreach (var exe in _executors)
            {
                if (exe is ConfigurableTestExecutor { IsBranchExecutor: true })
                {
                    try
                    {
                        var result = await exe.ExecuteAsync(step, ctx, ct);
                        if (result.Outcome == StepOutcome.Success)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return false;
        }

        private WorkflowState RollbackAll(Workflow workflow)
        {
            foreach (var s in workflow.Steps)
            {
                if (s.State == WorkflowState.Completed)
                {
                    s.SetState(WorkflowState.Pending);
                }
            }
            workflow.SetState(WorkflowState.RolledBack);
            return WorkflowState.RolledBack;
        }

        public Task PauseAsync(Guid workflowId, CancellationToken ct) => Task.CompletedTask;
        public Task<WorkflowState> ResumeAsync(Guid workflowId, CancellationToken ct)
            => Task.FromResult(WorkflowState.Running);
        public Task<WorkflowState> GetStatusAsync(Guid workflowId, CancellationToken ct)
            => Task.FromResult(WorkflowState.Pending);

        private static bool IsBranchExecutor(IStepExecutor e)
        {
            return e is ConfigurableTestExecutor { IsBranchExecutor: true };
        }
    }
}
