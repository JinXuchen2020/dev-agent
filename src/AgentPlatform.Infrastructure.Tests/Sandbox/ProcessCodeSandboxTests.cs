#nullable disable
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Sandbox;

public class ProcessCodeSandboxTests
{
    private static ProcessCodeSandbox Sandbox(int timeout = 30, bool network = false)
    {
        var settings = new SandboxSettings
        {
            Provider = "Process",
            TimeoutSeconds = timeout,
            AllowedLanguages = new[] { "python", "javascript", "csscript" },
            NetworkEnabled = network,
            MaxOutputBytes = 65536,
        };
        return new ProcessCodeSandbox(Substitute.For<ILogger<ProcessCodeSandbox>>(), Options.Create(settings));
    }

    [Fact]
    public async Task RunCodeAsync_Python_Prints_To_Stdout()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("print('hello from sandbox')", "python", 30, default);
        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello from sandbox", r.Stdout);
    }

    [Fact]
    public async Task RunCodeAsync_Javascript_Works()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("console.log('js hello')", "javascript", 30, default);
        Assert.True(r.Success);
        Assert.Contains("js hello", r.Stdout);
    }

    [Fact]
    public async Task RunCodeAsync_RuntimeError_Returns_Failure_With_Stderr()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("raise ValueError('boom')", "python", 30, default);
        Assert.False(r.Success);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("boom", r.Stderr);
    }

    [Fact]
    public async Task RunCodeAsync_DisallowedLanguage_Returns_Failure()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("echo hi", "ruby", 30, default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task RunCodeAsync_Timeout_KillsProcess_And_Fails()
    {
        var sb = Sandbox(timeout: 2);
        var sw = Stopwatch.StartNew();
        var r = await sb.RunCodeAsync("import time\ntime.sleep(30)", "python", 2, default);
        sw.Stop();
        Assert.False(r.Success);
        Assert.True(sw.ElapsedMilliseconds < 15000, $"sandbox 不应挂起；实际耗时={sw.ElapsedMilliseconds}ms");
        Assert.Contains("超时", r.Stderr);
    }
}
