using System.Diagnostics;
using System.Text;
using AgentPlatform.Application.Abstractions;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 容器级代码沙箱：经 <see cref="Docker.DotNet"/> 真实拉起隔离容器执行代码 / 命令，
/// 相较进程沙箱（<see cref="ProcessCodeSandbox"/>）提供更强文件系统 / 网络 / 资源边界。
/// 仅在 <c>Sandbox:Provider=Docker</c> 时经 DI 注册（见 <c>DependencyInjection.cs</c>）。
/// 默认 <c>Sandbox:Provider=Process</c>，无 Docker 环境自动回退进程沙箱。
/// </summary>
internal sealed class DockerCodeSandbox : ICodeSandbox
{
    private readonly ILogger<DockerCodeSandbox> _logger;
    private readonly SandboxSettings _settings;

    // 资源上限常量（后续如需可经 SandboxSettings 暴露）。
    private const long MemoryBytes = 256L * 1024 * 1024;  // 256 MB

    public DockerCodeSandbox(ILogger<DockerCodeSandbox> logger, IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<SandboxResult> RunCodeAsync(string code, string language,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var lang = (language ?? string.Empty).ToLowerInvariant();
        if (!_settings.AllowedLanguages.Contains(lang))
            return new SandboxResult(false, string.Empty, $"未找到 {language} 解释器或未授权", 1, 0);
        if (lang == "csscript")
            return new SandboxResult(false, string.Empty, "Docker 沙箱暂不支持 csscript（请改用 Process 提供方）", 1, 0);

        if (!TryMapLanguage(lang, out var image, out var ext))
            return new SandboxResult(false, string.Empty, $"Docker 沙箱不支持语言 {language}", 1, 0);

        var tempFile = Path.Combine(Path.GetTempPath(), $"ap_docker_{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllTextAsync(tempFile, code ?? string.Empty, Encoding.UTF8, ct).ConfigureAwait(false);
            var cmd = lang == "python" ? new[] { "python", $"/sandbox/code{ext}" }
                                       : new[] { "node", $"/sandbox/code{ext}" };
            var binds = new[] { $"{tempFile}:/sandbox/code{ext}:ro" };
            return await RunInContainerAsync(image, cmd, binds, timeoutSeconds, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Docker 沙箱运行 {Lang} 代码异常", lang);
            return new SandboxResult(false, string.Empty, $"执行异常: {ex.Message}", 1, 0);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    public async Task<SandboxResult> RunCommandAsync(string command,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new SandboxResult(false, string.Empty, "命令为空", 1, 0);
        try
        {
            return await RunInContainerAsync(
                "alpine:3.20",
                new[] { "sh", "-c", command },
                Array.Empty<string>(),
                timeoutSeconds, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Docker 沙箱运行命令异常");
            return new SandboxResult(false, string.Empty, $"执行异常: {ex.Message}", 1, 0);
        }
    }

    private async Task<SandboxResult> RunInContainerAsync(
        string image, string[] cmd, string[] binds, int timeoutSeconds, CancellationToken ct)
    {
        using var client = new DockerClientConfiguration().CreateClient();
        var sw = Stopwatch.StartNew();

        string containerId;
        var create = BuildContainerConfig(image, cmd, binds);
        try
        {
            try
            {
                var created = await client.Containers.CreateContainerAsync(create, ct).ConfigureAwait(false);
                containerId = created.ID;
            }
            catch (DockerImageNotFoundException)
            {
                // 镜像缺失：拉取后重试一次。
                await PullImageAsync(client, image, ct).ConfigureAwait(false);
                var created = await client.Containers.CreateContainerAsync(create, ct).ConfigureAwait(false);
                containerId = created.ID;
            }
        }
        catch (DockerApiException ex)
        {
            return new SandboxResult(false, string.Empty, $"创建容器失败: {ex.Message}", 1, sw.ElapsedMilliseconds);
        }

        bool timedOut = false;
        long exitCode = -1;
        (string Stdout, string Stderr) logs = default;
        try
        {
            await client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct)
                .ConfigureAwait(false);

            var waitTask = client.Containers.WaitContainerAsync(containerId, CancellationToken.None);
            var timeoutTask = Task.Delay(
                TimeSpan.FromSeconds(timeoutSeconds <= 0 ? _settings.TimeoutSeconds : timeoutSeconds), ct);
            if (await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
            {
                // 区分真实超时与调用方取消：取消应向上传播 OperationCanceledException，
                // 而非伪装成超时失败结果（否则 CodeStepExecutor 无法区分"被取消"与"执行超时"）。
                if (ct.IsCancellationRequested)
                {
                    await client.Containers.KillContainerAsync(
                        containerId, new ContainerKillParameters(), CancellationToken.None).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }

                timedOut = true;
                await client.Containers.KillContainerAsync(
                    containerId, new ContainerKillParameters(), CancellationToken.None).ConfigureAwait(false);
                try { await waitTask.ConfigureAwait(false); } catch { /* 已 kill */ }
            }

            var response = waitTask.IsCompletedSuccessfully ? await waitTask.ConfigureAwait(false) : null;
            exitCode = response?.StatusCode ?? -1;
        }
        finally
        {
            logs = await SafeReadLogsAsync(client, containerId).ConfigureAwait(false);
            await SafeRemoveAsync(client, containerId).ConfigureAwait(false);
        }

        var outStr = Truncate(logs.Stdout, _settings.MaxOutputBytes);
        var errStr = Truncate(logs.Stderr, _settings.MaxOutputBytes);
        var success = !timedOut && exitCode == 0;
        var finalErr = timedOut ? $"执行超时（>{timeoutSeconds}s）\n{errStr}" : errStr;
        _logger.LogInformation(
            "Docker 沙箱执行完成：Success={Success} ExitCode={ExitCode} Duration={Duration}ms",
            success, exitCode, sw.ElapsedMilliseconds);
        return new SandboxResult(success, outStr, finalErr, (int)exitCode, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// 构建容器创建参数（纯函数，便于单测命令构造，不依赖 Docker 守护进程）。
    /// </summary>
    internal CreateContainerParameters BuildContainerConfig(string image, string[] cmd, string[] binds)
    {
        var networkMode = _settings.NetworkEnabled ? "default" : "none";
        return new CreateContainerParameters
        {
            Image = image,
            Cmd = cmd,
            WorkingDir = "/sandbox",
            // Tty=true：日志为纯文本（无多路复用帧），便于 GetContainerLogs 直接读为合并文本。
            Tty = true,
            HostConfig = new HostConfig
            {
                Binds = binds,
                NetworkMode = networkMode,
                Memory = MemoryBytes,
                AutoRemove = false,
                ReadonlyRootfs = false,
            },
        };
    }

    private static bool TryMapLanguage(string language, out string image, out string ext)
    {
        switch (language)
        {
            case "python":
                image = "python:3.12-slim";
                ext = ".py";
                return true;
            case "javascript":
                image = "node:20-slim";
                ext = ".js";
                return true;
            default:
                image = string.Empty;
                ext = ".txt";
                return false;
        }
    }

    private async Task PullImageAsync(IDockerClient client, string image, CancellationToken ct)
    {
        var (repo, tag) = SplitImage(image);
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repo, Tag = tag },
            null,
            new Progress<JSONMessage>(),
            ct).ConfigureAwait(false);
    }

    private static (string Repo, string Tag) SplitImage(string image)
    {
        var idx = image.LastIndexOf(':');
        // 仅当 ':' 之后不含 '/' 时才视为 tag（避免误拆 registry:port/repo:tag）。
        if (idx > 0 && image.IndexOf('/', idx) < 0)
            return (image.Substring(0, idx), image.Substring(idx + 1));
        return (image, "latest");
    }

    private async Task<(string Stdout, string Stderr)> SafeReadLogsAsync(IDockerClient client, string id)
    {
        try
        {
            using var stream = await client.Containers.GetContainerLogsAsync(
                id, true, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, CancellationToken.None)
                .ConfigureAwait(false);
            using var ms = new MemoryStream();
            // tty 参数必须与容器 Tty=true 一致：否则 MultiplexedStream 会按多路复用帧去解析纯文本，
            // 导致输出被截断为空（这正是 ubuntu-latest CI 上集成测试 stdout 为空的根因）。
            // Tty=true 时 stdout/stderr 已合并为单一裸流，全部写入 ms。
            await stream.CopyOutputToAsync(null, ms, null, CancellationToken.None).ConfigureAwait(false);
            ms.Position = 0;
            using var reader = new StreamReader(ms);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            return (text, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取容器日志失败 {Id}", id);
            return (string.Empty, string.Empty);
        }
    }

    private async Task SafeRemoveAsync(IDockerClient client, string id)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(
                id, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "移除容器失败 {Id}", id);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* 临时文件清理失败不阻断 */ }
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxBytes) return value;
        return value.Substring(0, maxBytes);
    }
}
