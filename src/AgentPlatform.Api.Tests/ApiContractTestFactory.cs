using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using AgentPlatform.Infrastructure.Persistence;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// 测试专用 ITenantModelClientResolver：恒返回空列表 → 回退平台 stub 模型。
/// 隔离在测试组合根做（生产解析器不读 Provider 配置；仅 Test 环境允许 Stub）。
/// </summary>
public sealed class StubTenantModelClientResolver : ITenantModelClientResolver
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TenantModelResolution>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantModelResolution>>(Array.Empty<TenantModelResolution>());
}

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> for API contract tests.
///
/// Overrides configuration to use an in-memory SQLite database, configures
/// a known JWT secret key for test token generation, and uses stub model
/// clients so the full ASP.NET Core pipeline runs without external dependencies.
/// </summary>
public class ApiContractTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly bool _queueMode;
    private readonly int _queueWaitTimeoutSeconds;

    /// <summary>
    /// JWT secret key used for signing test tokens. Must be at least 32 characters
    /// and differ from the dev default to satisfy the startup guard in Program.cs.
    /// </summary>
    private const string TestJwtSecretKey = "test-only-secret-key-at-least-32-chars!!";

    private readonly SqliteConnection _sqliteConnection;

    /// <summary>
    /// Initializes a new factory instance and opens a shared in-memory
    /// SQLite connection that lives for the lifetime of the factory.
    /// </summary>
    public ApiContractTestFactory() : this(queueMode: false)
    {
    }

    /// <summary>
    /// <paramref name="queueMode"/> = true 时启用 F37 队列模式（InMemory 后端 + worker 消费），
    /// 用于端到端验证「入队 → worker 执行 → 等待窗口内返回终态」的完整链路。
    /// protected：xUnit 类夹具要求唯一公共构造函数，队列变体经派生类暴露。
    /// </summary>
    protected ApiContractTestFactory(bool queueMode, int queueWaitTimeoutSeconds = 20)
    {
        _queueMode = queueMode;
        _queueWaitTimeoutSeconds = queueWaitTimeoutSeconds;
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    /// <summary>
    /// Configures the web host to use the Test environment, overrides settings,
    /// and replaces the EF Core database provider with the in-memory SQLite
    /// connection shared across all requests.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use in-memory SQLite for the database
                ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                ["Database:Type"] = "sqlite",

                // Pin the default tenant so the seeded admin user, the tenant
                // resolved by TenantProvider at login, and the test Bearer token
                // (which carries tenant_id 00000000-0000-0000-0000-000000000001)
                // all agree. Without this, the login endpoint could resolve a
                // different default tenant than the one the seed wrote the user to.
                ["Tenant:DefaultTenantId"] = "00000000-0000-0000-0000-000000000001",

                // Valid JWT secret key (must differ from dev default)
                ["Security:JwtSecretKey"] = TestJwtSecretKey,
                ["Security:DevLoginEnabled"] = "false",

                // Keep authentication enforced so the real auth pipeline runs
                ["Security:EnforceAuthentication"] = "true",

                // Use in-memory cache to avoid Redis dependency
                ["Cache:Provider"] = "Memory",

                // Use stub model client to avoid real LLM API calls
                ["ModelClient:Provider"] = "Stub",
                ["ModelClient:StubResponse"] = "Contract test response.",

                // Provide a non-empty Key so the embedding service registration does not throw
                ["OpenAI:Key"] = "test-openai-key-not-empty",

                // F37 队列模式（仅队列测试工厂启用）：InMemory 后端 + worker 同进程消费。
                ["DurableExecution:QueueEnabled"] = _queueMode ? "true" : "false",
                ["DurableExecution:QueueBackend"] = "InMemory",
                ["DurableExecution:QueueWaitTimeoutSeconds"] = _queueWaitTimeoutSeconds.ToString(),
                ["DurableExecution:QueuePollIntervalSeconds"] = "1",
                ["DurableExecution:WorkerIdleDelayMilliseconds"] = "100",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the default DbContext options registration so we can
            // inject the shared in-memory SQLite connection.
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_sqliteConnection));

            // 租户 BYO 模型解析替换为恒空 stub（组合根隔离，防假凭据触发真实出站）。
            var resolverDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ITenantModelClientResolver));
            if (resolverDescriptor is not null)
                services.Remove(resolverDescriptor);
            services.AddScoped<ITenantModelClientResolver, StubTenantModelClientResolver>();

            // Create the database schema using a temporary scope so the
            // schema is ready before the first test request.
            // The same _sqliteConnection is used by the host's DI container,
            // so EnsureCreated runs against the same in-memory database.
            using var tempSp = services.BuildServiceProvider();
            using var tempScope = tempSp.CreateScope();
            var db = tempScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Factory initialization — ensures the server is started and database schema ready.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Ensure the database schema exists by creating a scope from
        // the host's DI container (which uses the shared _sqliteConnection).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await _sqliteConnection.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sqliteConnection?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> pre-configured with a valid
    /// Bearer JWT token in the default request headers.
    /// </summary>
    /// <param name="role">The role claim to include (default: "Admin").</param>
    /// <returns>An HttpClient that sends authenticated requests.</returns>
    public HttpClient CreateAuthenticatedClient(string role = "Admin")
    {
        var client = CreateClient();
        var token = GenerateTestToken(role);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Generates a JWT bearer token signed with the test secret key.
    /// Includes role, name identifier, and tenant claims.
    /// </summary>
    private static string GenerateTestToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", "00000000-0000-0000-0000-000000000001"),
        };

        var token = new JwtSecurityToken(
            issuer: "agent-platform",
            audience: "agent-platform-api",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// F37 队列模式 API 夹具（xUnit 夹具类型需唯一公共构造函数，故以派生类暴露）。
/// </summary>
public sealed class QueueModeApiContractTestFactory : ApiContractTestFactory
{
    /// <summary>启用 InMemory 队列后端 + 同进程 worker 的测试工厂。</summary>
    public QueueModeApiContractTestFactory() : base(queueMode: true)
    {
    }
}

/// <summary>
/// 脚本化假队列（F37 Api 契约 fixture）：只控制入队结局与消费可见性，
/// 用于在真 HTTP 管线上确定性地触发「拒投 → 503」与「等待超时 → 202」分支。
/// </summary>
public sealed class ScriptedApiExecutionQueue : IExecutionQueue
{
    private readonly bool _acceptEnqueue;

    /// <summary>入队时拒绝（模拟队列满/后端不可用）。</summary>
    public static ScriptedApiExecutionQueue Rejecting() => new(acceptEnqueue: false);

    /// <summary>入队接受但永不投递（模拟 worker 停摆/后端吞消息）→ run 等待窗口必然超时。</summary>
    public static ScriptedApiExecutionQueue Stalled() => new(acceptEnqueue: true);

    private ScriptedApiExecutionQueue(bool acceptEnqueue)
    {
        _acceptEnqueue = acceptEnqueue;
    }

    /// <summary>记录被拒的入队尝试，供测试断言任务显式到达了队列接缝（而非中途丢失）。</summary>
    public List<ExecutionJob> EnqueueAttempts { get; } = [];

    /// <inheritdoc />
    public string Backend => "Scripted";

    /// <inheritdoc />
    public Task<bool> ProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default)
    {
        EnqueueAttempts.Add(job);
        return Task.FromResult(_acceptEnqueue ? EnqueueResult.Enqueued : EnqueueResult.RejectedQueueFull);
    }

    /// <inheritdoc />
    public Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default)
        => Task.FromResult<QueueDelivery?>(null);

    /// <inheritdoc />
    public Task CompleteAsync(string receipt, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default)
        => Task.FromResult(true);
}

public abstract class QueueContractApiContractTestFactory : ApiContractTestFactory
{
    /// <summary>该夹具宿主使用的假队列实例（测试经此断言入队确实到达队列接缝）。</summary>
    public ScriptedApiExecutionQueue Queue { get; }

    /// <summary>基类接缝：派生队列契约夹具在此替换/装饰 <see cref="IExecutionQueue"/> 注册。</summary>
    protected QueueContractApiContractTestFactory(Func<IExecutionQueue> queueFactory, int queueWaitTimeoutSeconds)
        : base(queueMode: true, queueWaitTimeoutSeconds)
    {
        Queue = (ScriptedApiExecutionQueue)queueFactory();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // 与基类同款延迟注册（在应用 ConfigureServices 之后执行）：移除真 InMemory 队列，
        // 以假队列顶替（worker 读不到投递，只影响消费侧，不影响 503/202 契约分支的确定性）。
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IExecutionQueue));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IExecutionQueue>(Queue);
        });
    }
}

/// <summary>F37 契约夹具：入队恒被拒（队列满/后端不可用）→ run 端点必须 503，绝不假成功。</summary>
public sealed class QueueRejectingApiContractTestFactory : QueueContractApiContractTestFactory
{
    /// <summary>使用恒拒投的假队列。</summary>
    public QueueRejectingApiContractTestFactory()
        : base(() => ScriptedApiExecutionQueue.Rejecting(), queueWaitTimeoutSeconds: 5)
    {
    }
}

/// <summary>F37 契约夹具：接受入队但永不消费 → 等待窗口超时，run 端点必须确定性返回 202 queued。</summary>
public sealed class QueueStalledApiContractTestFactory : QueueContractApiContractTestFactory
{
    /// <summary>使用停摆（不投递）的假队列，等待窗口 1s。</summary>
    public QueueStalledApiContractTestFactory()
        : base(() => ScriptedApiExecutionQueue.Stalled(), queueWaitTimeoutSeconds: 1)
    {
    }
}
