#nullable disable
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Sandbox;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Sandbox;

public class DockerCodeSandboxTests
{
    private static DockerCodeSandbox Sandbox(bool network = false)
    {
        var settings = new SandboxSettings
        {
            Provider = "Docker",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python", "javascript", "csscript" },
            NetworkEnabled = network,
            MaxOutputBytes = 65536,
        };
        return new DockerCodeSandbox(Substitute.For<ILogger<DockerCodeSandbox>>(), Options.Create(settings));
    }

    // ── 纯函数单测（无需 Docker 守护进程）──

    [Fact]
    public void BuildContainerConfig_Python_SetsImage_Cmd_Binds_And_ResourceLimits()
    {
        var sb = Sandbox();
        var cfg = sb.BuildContainerConfig("python:3.12-slim", new[] { "python", "/sandbox/code.py" },
            new[] { "/tmp/x.py:/sandbox/code.py:ro" });

        Assert.Equal("python:3.12-slim", cfg.Image);
        Assert.Contains("python", cfg.Cmd);
        Assert.Contains("/sandbox/code.py", cfg.Cmd);
        Assert.True(cfg.Tty);
        Assert.Contains("/tmp/x.py:/sandbox/code.py:ro", cfg.HostConfig.Binds);
        Assert.Equal("none", cfg.HostConfig.NetworkMode); // NetworkEnabled=false → 禁网
        Assert.Equal(256L * 1024 * 1024, cfg.HostConfig.Memory);
        Assert.False(cfg.HostConfig.AutoRemove);
    }

    [Fact]
    public void BuildContainerConfig_NetworkEnabled_UsesDefaultNetworkMode()
    {
        var sb = Sandbox(network: true);
        var cfg = sb.BuildContainerConfig("node:20-slim", new[] { "node", "/sandbox/code.js" },
            new[] { "/tmp/x.js:/sandbox/code.js:ro" });

        Assert.Equal("node:20-slim", cfg.Image);
        Assert.Equal("default", cfg.HostConfig.NetworkMode);
    }

    [Fact]
    public async Task RunCodeAsync_DisallowedLanguage_ReturnsFailure_WithoutDockerCall()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("echo hi", "ruby", 30, default);
        Assert.False(r.Success);
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public async Task RunCodeAsync_Csscript_UnsupportedInDocker()
    {
        var sb = Sandbox();
        var r = await sb.RunCodeAsync("Console.WriteLine(1)", "csscript", 30, default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task RunCommandAsync_EmptyCommand_ReturnsFailure()
    {
        var sb = Sandbox();
        var r = await sb.RunCommandAsync("", 30, default);
        Assert.False(r.Success);
    }

    // ── 集成单测（需 Docker 守护进程；不可达时跳过）──

    [SkippableFact]
    public async Task RunCodeAsync_Python_Runs_In_Isolated_Container()
    {
        Skip.IfNot(DockerAvailable(), "Docker 守护进程不可达，跳过真实容器集成测试（需在含 Docker 的 CI 运行）");

        var sb = Sandbox();
        var r = await sb.RunCodeAsync("print('docker_ok')", "python", 60, default);
        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("docker_ok", r.Stdout);
    }

    [SkippableFact]
    public async Task RunCommandAsync_ShellCommand_Runs_In_Alpine()
    {
        Skip.IfNot(DockerAvailable(), "Docker 守护进程不可达，跳过真实容器集成测试（需在含 Docker 的 CI 运行）");

        var sb = Sandbox();
        var r = await sb.RunCommandAsync("echo container_cmd_ok", 60, default);
        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("container_cmd_ok", r.Stdout);
    }

    [SkippableFact]
    public async Task RunCodeAsync_Timeout_KillsLongRunningContainer()
    {
        Skip.IfNot(DockerAvailable(), "Docker 守护进程不可达，跳过真实容器集成测试（需在含 Docker 的 CI 运行）");

        var sb = Sandbox();
        // 50s 睡眠，超时设为 3s → 应被 kill，Success=false 且 ExitCode≠0。
        var r = await sb.RunCodeAsync("import time\ntime.sleep(50)\nprint('should_not_appear')", "python", 3, default);
        Assert.False(r.Success);
        Assert.NotEqual(0, r.ExitCode);
    }

    private static bool DockerAvailable()
    {
        try
        {
            using var client = new DockerClientConfiguration().CreateClient();
            client.Containers.ListContainersAsync(new ContainersListParameters { Limit = 1 })
                .GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
