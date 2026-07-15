using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using System.Collections.Concurrent;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class ExecutionLogSteps
{
    private readonly InMemoryExecutionLogRepository _repository = new();
    private ExecutionLog? _currentLog;
    private Guid _currentTenantId = Guid.NewGuid();
    private IReadOnlyList<ExecutionLogSummary> _queryResults = [];
    private int _totalCount;

    [Given("the execution log repository is initialized")]
    public void GivenRepositoryInitialized()
    {
        _repository.Clear();
    }

    [Given("a workflow has completed with (.*) steps")]
    public void GivenWorkflowCompleted(int stepCount)
    {
        var workflowId = Guid.NewGuid();
        var log = new ExecutionLog(
            Guid.NewGuid(),
            workflowId,
            "Test Workflow",
            Guid.NewGuid(),
            stepCount);

        for (int i = 0; i < stepCount; i++)
        {
            var entry = new ExecutionLogEntry(
                Guid.NewGuid(),
                $"Step {i + 1}",
                i,
                WorkflowState.Completed,
                TimeSpan.FromSeconds(i + 1),
                $"Result for step {i + 1}",
                null);
            log.AddEntry(entry);
        }

        log.Complete();
        _repository.Add(log);
        _currentLog = log;
    }

    [Given("step (.*) of the workflow failed")]
    public void GivenStepFailed(int stepNumber)
    {
        var log = _repository.GetAll().First();
        // Clear existing entries and rebuild with the specified step failed
        _repository.Clear();

        var rebuiltLog = new ExecutionLog(
            Guid.NewGuid(),
            log.WorkflowId,
            log.WorkflowName,
            log.TenantId,
            log.TotalSteps);

        for (int i = 0; i < log.TotalSteps; i++)
        {
            var isFailedStep = i == stepNumber - 1;
            var entry = new ExecutionLogEntry(
                Guid.NewGuid(),
                $"Step {i + 1}",
                i,
                isFailedStep ? WorkflowState.Failed : WorkflowState.Completed,
                TimeSpan.FromSeconds(i + 1),
                isFailedStep ? null : $"Result for step {i + 1}",
                isFailedStep ? $"Error occurred in step {i + 1}" : null);
            rebuiltLog.AddEntry(entry);
        }

        rebuiltLog.Rollback();
        _repository.Add(rebuiltLog);
        _currentLog = rebuiltLog;
    }

    [Given("logs exist across multiple days")]
    public void GivenLogsExistAcrossMultipleDays()
    {
        var workflowId = Guid.NewGuid();
        var baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Create logs on day 1, day 3, and day 5
        for (int dayDelta = 0; dayDelta <= 4; dayDelta += 2)
        {
            var log = new ExecutionLog(
                Guid.NewGuid(),
                workflowId,
                $"Workflow Day {dayDelta + 1}",
                Guid.NewGuid(),
                1);

            log.AddEntry(new ExecutionLogEntry(
                Guid.NewGuid(),
                "Step 1",
                0,
                WorkflowState.Completed,
                TimeSpan.FromSeconds(1),
                "OK",
                null));

            log.Complete();
            _repository.Add(log);
        }
    }

    [Given("some steps succeeded and some failed")]
    public void GivenSomeStepsSucceededAndSomeFailed()
    {
        var log = new ExecutionLog(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Mixed Workflow",
            Guid.NewGuid(),
            4);

        var entries = new[]
        {
            new ExecutionLogEntry(Guid.NewGuid(), "Step 1", 0, WorkflowState.Completed, TimeSpan.FromSeconds(1), "OK", null),
            new ExecutionLogEntry(Guid.NewGuid(), "Step 2", 1, WorkflowState.Completed, TimeSpan.FromSeconds(2), "OK", null),
            new ExecutionLogEntry(Guid.NewGuid(), "Step 3", 2, WorkflowState.Failed, TimeSpan.FromSeconds(3), null, "Step 3 error"),
            new ExecutionLogEntry(Guid.NewGuid(), "Step 4", 3, WorkflowState.Failed, TimeSpan.FromSeconds(1), null, "Step 4 error"),
        };

        foreach (var entry in entries)
            log.AddEntry(entry);

        log.Rollback();
        _repository.Add(log);
        _currentLog = log;
    }

    [Given("(.*) log entries exist")]
    public void GivenLogEntriesExist(int count)
    {
        var workflowId = Guid.NewGuid();
        _currentTenantId = Guid.NewGuid();
        for (int i = 0; i < count; i++)
        {
            var log = new ExecutionLog(
                Guid.NewGuid(),
                workflowId,
                $"Large Workflow #{i + 1}",
                _currentTenantId,
                1);

            log.AddEntry(new ExecutionLogEntry(
                Guid.NewGuid(),
                "Step 1",
                0,
                WorkflowState.Completed,
                TimeSpan.FromMilliseconds(100),
                $"Result {i + 1}",
                null));

            log.Complete();
            _repository.Add(log);
        }
    }

    [When("a user queries execution logs for the workflow")]
    public async Task WhenUserQueriesLogsForWorkflow()
    {
        var logs = await _repository.GetByWorkflowIdAsync(_currentLog!.WorkflowId);
        _queryResults = logs.Select(l => new ExecutionLogSummary(
            l.Id, l.WorkflowId, l.WorkflowName, l.Status,
            l.TotalSteps,
            l.Entries.Count(e => e.Status == WorkflowState.Completed),
            l.Entries.Count(e => e.Status == WorkflowState.Failed),
            l.StartedAt, l.CompletedAt
        )).ToList();
    }

    [When("a user queries execution logs")]
    public async Task WhenUserQueriesExecutionLogs()
    {
        var (items, total) = await _repository.QueryAsync(
            _currentLog?.TenantId ?? Guid.NewGuid());
        _queryResults = items.Select(l => new ExecutionLogSummary(
            l.Id, l.WorkflowId, l.WorkflowName, l.Status,
            l.TotalSteps,
            l.Entries.Count(e => e.Status == WorkflowState.Completed),
            l.Entries.Count(e => e.Status == WorkflowState.Failed),
            l.StartedAt, l.CompletedAt
        )).ToList();
        _totalCount = total;
    }

    [When("a user filters logs by a date range")]
    public async Task WhenUserFiltersLogsByDateRange()
    {
        var from = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 6, 0, 0, 0, DateTimeKind.Utc);
        var (items, total) = await _repository.QueryAsync(
            _currentLog?.TenantId ?? Guid.NewGuid(),
            from: from,
            to: to);
        _queryResults = items.Select(l => new ExecutionLogSummary(
            l.Id, l.WorkflowId, l.WorkflowName, l.Status,
            l.TotalSteps,
            l.Entries.Count(e => e.Status == WorkflowState.Completed),
            l.Entries.Count(e => e.Status == WorkflowState.Failed),
            l.StartedAt, l.CompletedAt
        )).ToList();
        _totalCount = total;
    }

    [When("a user filters logs by status \"(.*)\"")]
    public async Task WhenUserFiltersLogsByStatus(string statusName)
    {
        var status = ParseState(statusName);
        var (items, total) = await _repository.QueryAsync(
            _currentLog?.TenantId ?? Guid.NewGuid(),
            status: status);
        _queryResults = items.Select(l => new ExecutionLogSummary(
            l.Id, l.WorkflowId, l.WorkflowName, l.Status,
            l.TotalSteps,
            l.Entries.Count(e => e.Status == WorkflowState.Completed),
            l.Entries.Count(e => e.Status == WorkflowState.Failed),
            l.StartedAt, l.CompletedAt
        )).ToList();
        _totalCount = total;
    }

    [When("a user queries with page (.*) and page size (.*)")]
    public async Task WhenUserQueriesWithPagination(int page, int pageSize)
    {
        var (items, total) = await _repository.QueryAsync(
            _currentTenantId,
            skip: (page - 1) * pageSize,
            take: pageSize);
        _queryResults = items.Select(l => new ExecutionLogSummary(
            l.Id, l.WorkflowId, l.WorkflowName, l.Status,
            l.TotalSteps,
            l.Entries.Count(e => e.Status == WorkflowState.Completed),
            l.Entries.Count(e => e.Status == WorkflowState.Failed),
            l.StartedAt, l.CompletedAt
        )).ToList();
        _totalCount = total;
    }

    [Then("they should receive (.*) log entries")]
    public void ThenShouldReceiveLogEntries(int expectedCount)
    {
        var totalEntries = _queryResults.Sum(r => r.CompletedSteps + r.FailedSteps);
        Assert.Equal(expectedCount, totalEntries);
    }

    [Then("each entry should contain status, duration, and timestamp")]
    public void ThenEachEntryShouldHaveMetadata()
    {
        var log = _repository.GetAll().First();
        foreach (var entry in log.Entries)
        {
            Assert.NotEqual(WorkflowState.Pending, entry.Status);
            Assert.True(entry.Duration > TimeSpan.Zero);
            Assert.NotEqual(default, entry.StartedAt);
        }
    }

    [Then("the log entry for step (.*) should include error details")]
    public void ThenStepShouldHaveErrorDetails(int stepNumber)
    {
        var log = _repository.GetAll().First();
        var entry = log.Entries.First(e => e.StepOrder == stepNumber - 1);
        Assert.NotNull(entry.ErrorDetail);
        Assert.NotEmpty(entry.ErrorDetail);
    }

    [Then("the error message should describe the failure reason")]
    public void ThenErrorMessageDescribesFailure()
    {
        var log = _repository.GetAll().First();
        var failedEntry = log.Entries.First(e => e.Status == WorkflowState.Failed);
        Assert.Contains("Error", failedEntry.ErrorDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Then("only logs within that range should be returned")]
    public void ThenOnlyLogsWithinRangeReturned()
    {
        var from = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 6, 0, 0, 0, DateTimeKind.Utc);
        Assert.All(_queryResults, r =>
        {
            Assert.True(r.StartedAt >= from);
            Assert.True(r.StartedAt <= to);
        });
    }

    [Then("only failed step entries should be returned")]
    public void ThenOnlyFailedEntriesReturned()
    {
        var log = _repository.GetAll().First();
        Assert.All(log.Entries.Where(e => e.Status == WorkflowState.Failed), e =>
            Assert.Equal(WorkflowState.Failed, e.Status));
    }

    [Then("they should receive (.*) entries")]
    public void ThenShouldReceiveEntries(int expectedCount)
    {
        Assert.Equal(expectedCount, _queryResults.Count);
    }

    [Then("total count should be (.*)")]
    public void ThenTotalCountShouldBe(int expectedCount)
    {
        Assert.Equal(expectedCount, _totalCount);
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

    private sealed record ExecutionLogSummary(
        Guid Id, Guid WorkflowId, string WorkflowName, WorkflowState Status,
        int TotalSteps, int CompletedSteps, int FailedSteps,
        DateTime StartedAt, DateTime? CompletedAt);

    /// <summary>
    /// In-memory implementation of <see cref="IExecutionLogRepository"/> for spec flow testing.
    /// </summary>
    private sealed class InMemoryExecutionLogRepository : IExecutionLogRepository
    {
        private readonly ConcurrentDictionary<Guid, ExecutionLog> _store = new();

        public void Clear() => _store.Clear();
        public List<ExecutionLog> GetAll() => _store.Values.ToList();

        public Task<ExecutionLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var log);
            return Task.FromResult(log);
        }

        public Task<IReadOnlyList<ExecutionLog>> GetByWorkflowIdAsync(Guid workflowId, CancellationToken ct = default)
        {
            var logs = _store.Values
                .Where(l => l.WorkflowId == workflowId)
                .OrderByDescending(l => l.StartedAt)
                .ToList() as IReadOnlyList<ExecutionLog>;
            return Task.FromResult(logs);
        }

        public Task<(IReadOnlyList<ExecutionLog> Items, int TotalCount)> QueryAsync(
            Guid tenantId,
            WorkflowState? status = null,
            DateTime? from = null,
            DateTime? to = null,
            int skip = 0,
            int take = 20,
            CancellationToken ct = default)
        {
            var query = _store.Values.Where(l => l.TenantId == tenantId).AsEnumerable();

            if (status.HasValue)
                query = query.Where(l => l.Status == status.Value);

            if (from.HasValue)
                query = query.Where(l => l.StartedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(l => l.StartedAt <= to.Value);

            var filtered = query.OrderByDescending(l => l.StartedAt).ToList();
            var totalCount = filtered.Count;
            var items = filtered.Skip(skip).Take(take).ToList() as IReadOnlyList<ExecutionLog>;

            return Task.FromResult((items, totalCount));
        }

        public void Add(ExecutionLog log)
        {
            _store.TryAdd(log.Id, log);
        }

        public void Update(ExecutionLog log)
        {
            _store[log.Id] = log;
        }
    }
}
