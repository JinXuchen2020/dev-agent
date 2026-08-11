using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using System.Threading.Tasks;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// F12 专用集成宿主：跨 Scenario / Feature 共享单个 <see cref="RealStepsIntegrationAppFactory"/>
/// （保留真实 Code/Tool 执行器的工厂变体）。与基 <see cref="IntegrationHost"/> 互相独立
/// （独立 DB 文件、独立服务器），避免与既有 BDD 争用或相互污染。
///
/// 生命周期由 <see cref="F12IntegrationHooks"/> 的 [BeforeTestRun]/[AfterTestRun] 管理：
/// 测试运行级仅初始化一次、释放一次。
/// </summary>
public static class F12IntegrationHost
{
    private static RealStepsIntegrationAppFactory? _factory;

    /// <summary>惰性创建并共享的 F12 集成工厂。</summary>
    public static RealStepsIntegrationAppFactory Factory => _factory ??= new RealStepsIntegrationAppFactory();

    /// <summary>已配置好基础地址的 HttpClient（真实管线，保留真实执行器）。</summary>
    public static HttpClient Api => Factory.Api;
}

/// <summary>
/// F12 生命周期钩子：测试运行级启动 / 释放 F12 宿主，并启动回环 echo 服务器、
/// 向 F12 容器的 IToolRegistry 注册 bdd-echo-tool（指向 echo 服务器）。
/// </summary>
[Binding]
public sealed class F12IntegrationHooks
{
    private static ToolEchoServer? _echoServer;

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        await F12IntegrationHost.Factory.InitializeAsync();

        // 启动回环 echo 服务器，并注册本地原生工具供 ToolStepExecutor 真实调用。
        _echoServer = new ToolEchoServer();
        var registry = F12IntegrationHost.Factory.Services.GetRequiredService<IToolRegistry>();
        registry.Register(new ToolDefinition(
            Guid.NewGuid(),
            "bdd-echo-tool",
            "F12 BDD loopback echo tool",
            "{}",
            "bdd-echo-tool",
            IntegrationConstants.Tenant1Id,
            ToolSource.NativeTool,
            _echoServer.BaseUrl));
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_echoServer is not null)
        {
            _echoServer.Dispose();
            _echoServer = null;
        }

        await F12IntegrationHost.Factory.DisposeAsync();
    }
}
