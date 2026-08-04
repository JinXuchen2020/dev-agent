using Reqnroll;
using System.Threading.Tasks;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 集成测试宿主：跨 Scenario / Feature 共享单个 <see cref="IntegrationAppFactory"/>（避免每个场景重建服务 + 重跑迁移）。
/// 整个测试运行仅初始化一次（[BeforeTestRun]）、释放一次（[AfterTestRun]）；
/// 各 Scenario 之间的数据隔离由各自 feature 的 Background reset 步骤保证（非此处）。
/// 注意：切勿在 [AfterFeature] 中释放 factory —— 静态 _factory 字段会残留已 disposed 实例，
/// 导致后续 feature 的 [BeforeFeature] 复用 disposed factory，SeedAsync 触发 ObjectDisposedException。
/// </summary>
public static class IntegrationHost
{
    private static IntegrationAppFactory? _factory;

    /// <summary>惰性创建并共享的集成工厂。</summary>
    public static IntegrationAppFactory Factory => _factory ??= new IntegrationAppFactory();

    /// <summary>已配置好的 HttpClient（真实管线）。</summary>
    public static HttpClient Api => Factory.Api;
}

/// <summary>
/// 生命周期钩子：测试运行级启动 / 释放集成宿主。
/// </summary>
[Binding]
public sealed class IntegrationHooks
{
    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        await IntegrationHost.Factory.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        await IntegrationHost.Factory.DisposeAsync();
    }
}
