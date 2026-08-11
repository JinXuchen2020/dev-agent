using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 最小回环 HTTP 响应器，供 F12 BDD 的 NativeToolExecutor 发起真实 HTTP 调用（无需外部网络）。
/// 监听 127.0.0.1 的 OS 分配空闲端口，对任意 GET/POST 返回固定 JSON
/// <c>{"echo":"ok","tool":"bdd-echo-tool"}</c>。
///
/// 选用 TcpListener 而非 HttpListener，以规避 Windows 上 HttpListener 的 URL ACL 配置问题
/// （见 features/tool-code-e2e.md §3.2 / 风险 R2）。<see cref="Dispose"/> 停止监听并取消循环，
/// 无端口 / 句柄泄漏。
/// </summary>
public sealed class ToolEchoServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    /// <summary>对外暴露的 base URL（含 OS 分配的动态端口），用作 ToolDefinition.EndpointUrl。</summary>
    public string BaseUrl { get; }

    public ToolEchoServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{port}";
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // 先排空请求头（GET 无 body；POST 体极小），确保客户端发送完毕后我们再回写响应，
                // 避免 Connection: close 下客户端仍在写时被重置。读到头结束符或 2s 超时即停止。
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromSeconds(2));
                var buffer = new byte[1024];
                var received = 0;
                try
                {
                    while (!readCts.IsCancellationRequested && received < 16 * 1024)
                    {
                        var read = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                        if (read == 0)
                            break;
                        received += read;
                        if (HeaderTerminatorReached(buffer, received))
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 读取超时——仍回写响应。
                }

                var body = "{\"echo\":\"ok\",\"tool\":\"bdd-echo-tool\"}";
                var response = "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + "Connection: close\r\n"
                    + "\r\n"
                    + body;
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch
        {
            // 忽略客户端侧错误（连接中断等），不影响其他请求。
        }
    }

    private static bool HeaderTerminatorReached(byte[] buffer, int length)
    {
        // 查找 \r\n\r\n（HTTP 头结束符）。
        for (var i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
            // 忽略停止异常。
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 忽略等待异常。
        }

        _cts.Dispose();
    }
}
