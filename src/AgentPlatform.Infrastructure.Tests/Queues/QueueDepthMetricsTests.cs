using System.Diagnostics.Metrics;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Infrastructure.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Queues;

/// <summary>
/// F39 队列积压观测验证：InMemory 后端的 <c>execution.queue.depth{backend}</c> ObservableGauge
/// 必须真实上报当前积压（MeterListener 断言测量值与标签），且深度随入队/消费变化。
/// 队列积压告警（QueueBacklogHigh）依赖该序列存在 —— 守「埋点确实可观测」这条底线。
/// </summary>
public sealed class QueueDepthMetricsTests
{
    private static ExecutionJob Job() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0);

    private static InProcessExecutionQueue NewQueue(int capacity = 4) =>
        new(Options.Create(new DurableExecutionSettings { QueueCapacity = capacity }),
            NullLogger<InProcessExecutionQueue>.Instance);

    [Fact]
    public async Task InProcess_QueueDepth_Reflects_Pending_Jobs()
    {
        using var queue = NewQueue();
        Assert.Equal(0, queue.QueueDepth);

        await queue.EnqueueAsync(Job());
        await queue.EnqueueAsync(Job());
        Assert.Equal(2, queue.QueueDepth);

        await queue.TryReadAsync();
        Assert.Equal(1, queue.QueueDepth);
    }

    [Fact]
    public async Task InProcess_Queue_Publishes_Depth_Gauge_With_Backend_Tag()
    {
        var seen = new List<(long Depth, string? Backend)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == WorkflowMetrics.Meter &&
                instrument.Name == WorkflowMetrics.QueueDepthInstrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            string? backend = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "backend")
                {
                    backend = tag.Value as string;
                }
            }

            seen.Add((value, backend));
        });

        // 先 Start 再构造队列：仪表发布事件才会被该监听器捕获。
        listener.Start();

        using var queue = NewQueue();
        await queue.EnqueueAsync(Job());
        await queue.EnqueueAsync(Job());

        listener.RecordObservableInstruments();

        Assert.Contains(seen, m => m.Backend == "InMemory" && m.Depth == 2);
    }

    [Fact]
    public void Redis_QueueDepth_Is_Safe_Without_Connection()
    {
        // 未建立连接时 QueueDepth 必须返回 0 而非抛出：scrape 路径不得因后端不可用而炸。
        var queue = new RedisStreamExecutionQueue(
            Options.Create(new DurableExecutionSettings()),
            new Microsoft.Extensions.Configuration.ConfigurationManager(),
            NullLogger<RedisStreamExecutionQueue>.Instance);

        Assert.Equal(0, queue.QueueDepth);
        Assert.Equal("RedisStream", queue.Backend);
    }
}
