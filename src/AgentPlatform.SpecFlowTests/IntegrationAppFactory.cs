using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 集成测试 WebApplicationFactory：以真实 HTTP 管线 + 真实文件 SQLite 数据库运行
/// AgentPlatform.Api。对应 BDD 集成层（设计文档 features/bdd-integration-design.md §4.2）。
///
/// 与 Api.Tests 的 in-memory SQLite 不同，本工厂使用独立磁盘文件 test-integration.db，
/// 仍走 EF 迁移 + 磁盘 I/O（满足「真 DB」契约）；并使用 Integration 环境令 DatabaseInitializer
/// 在启动时跑迁移 + 基础种子（角色 / agent 配置 / admin 用户）。
///
/// 类刻意「解封」（非 sealed）并抽出 3 个可覆写钩子（DbPath / StripStepExecutors /
/// IntegrationConfiguration），以便 F12 派生 <see cref="RealStepsIntegrationAppFactory"/>
/// 在保留真实 IStepExecutor（Code/Tool 真实执行）的同时不破坏既有 BDD（默认行为不变）。
/// </summary>
public class IntegrationAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>测试 JWT 密钥（≥32 字符且非 dev 默认值，满足 Program.cs 启动守卫）。</summary>
    private const string TestJwtSecretKey = "test-only-secret-key-at-least-32-chars!!";

    /// <summary>真实磁盘 SQLite 文件名（可覆写，避免派生工厂争用同一文件）。每次运行重建，仍具真实磁盘 I/O。</summary>
    protected virtual string DbPath => "test-integration.db";

    /// <summary>
    /// 是否剥除全部真实 IStepExecutor 并替换为 ConfigurableStepExecutor（假输出）。
    /// 默认 true（隔离外部 LLM 行为）；F12 覆写为 false 以保留真实 Code/Tool 执行器。
    /// </summary>
    protected virtual bool StripStepExecutors => true;

    /// <summary>
    /// 注入到宿主的配置（内存集合）。既有 BDD 的全部键值在此；派生工厂可覆写以追加键
    /// （如 F12 的 Sandbox:Provider=Process + 解释器路径）。
    /// </summary>
    protected virtual Dictionary<string, string?> IntegrationConfiguration => new()
    {
        // 真实文件 SQLite（非 in-memory）。Pooling=false 确保连接关闭后立即释放文件句柄，
        // 否则 AfterFeature 删除磁盘文件会因仍被连接池占用而抛 IOException。
        ["ConnectionStrings:DefaultConnection"] = $"Data Source={DbPath};Pooling=false",
        ["Database:Type"] = "sqlite",

        // 钉死默认租户，使种子 admin 用户、TenantProvider 解析、JWT 声明三方一致
        ["Tenant:DefaultTenantId"] = IntegrationConstants.Tenant1Id.ToString(),

        // 合法 JWT 密钥（非 dev 默认）
        ["Security:JwtSecretKey"] = TestJwtSecretKey,
        ["Security:DevLoginEnabled"] = "false",
        ["Security:EnforceAuthentication"] = "true",

        // Integration 环境关闭限流，避免令牌桶干扰 BDD 真 HTTP 验收场景
        // （features/bdd-integration-design.md §11 风险 2）。
        ["Security:RateLimitingEnabled"] = "false",

        // 内存缓存避免 Redis 依赖
        ["Cache:Provider"] = "Memory",

        // 从环境变量读取真实 LLM Key（CI 必须预置）；DeepSeek/vLLM 均兼容 OpenAI 协议，统一走 OpenAI 配置。
        // 平台默认模型由 DatabaseInitializer 在启动时从 OpenAI:* 环境变量种子化进 PlatformModels 表，
        // 不再依赖 Router:Candidates 配置（已移除）。
        // 注意：环境变量名即字面量 OPENAI_API_KEY（单下划线），CI (ci.yml) 必须以同名注入，
        // 双下划线 OPENAI__KEY 是 .NET 配置分隔符写法，作为字面量环境变量名此处读不到。
        ["OpenAI:Key"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
        ["OpenAI:Model"] = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini",
        ["OpenAI:BaseUrl"] = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "",
    };

    /// <summary>已配置好基础地址的 HttpClient（真实管线）。</summary>
    public HttpClient Api { get; private set; } = null!;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Integration");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(IntegrationConfiguration);
        });

        // 用文件 SQLite 覆盖默认 DbContext 注册（与 ApiContractTestFactory 同法，已验证）。
        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={DbPath};Pooling=false"));

            // 默认：用可控执行器替换全部真实 IStepExecutor（仅隔离外部 LLM 步骤行为，真实引擎 + 真 DB 不变）。
            // F12 通过 StripStepExecutors=false 保留真实执行器（见 RealStepsIntegrationAppFactory）。
            if (StripStepExecutors)
            {
                // 见 ConfigurableStepExecutor：这是 WorkflowStateMachine / MultiAgentPipeline 旧玩具假实现的诚实替代。
                var executorDescriptors = services.Where(d => d.ServiceType == typeof(IStepExecutor)).ToList();
                foreach (var d in executorDescriptors)
                    services.Remove(d);

                services.AddSingleton<ConfigurableStepExecutor>();
                services.AddSingleton<IStepExecutor>(sp => sp.GetRequiredService<ConfigurableStepExecutor>());
            }

            // 租户 BYO 模型解析在 DI 层替换为恒空实现：BDD 写入的假凭据（sk-bdd-test-*）不得触发
            // 真实 OpenAI 出站。隔离放在测试组合根而非生产解析器读配置——QuickStart 同为
            // Provider=Stub，但必须允许用户自配模型真实生效（2026-08-26 CI 401 修复）。
            var resolverDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ITenantModelClientResolver));
            if (resolverDescriptor is not null)
                services.Remove(resolverDescriptor);
            services.AddScoped<ITenantModelClientResolver, StubTenantModelClientResolver>();

            // 禁用后台托管服务（执行日志清理 / ApiKey 过期 / 工作流调度定时任务）：
            // 它们会周期性写同一文件 SQLite，与 BDD 场景并发写引发 database is locked → 21s 忙等 → 偶发 500。
            // 功能 BDD 不依赖这些定时器，关闭即可消除该并发变量（设计文档 §11 风险 3）。
            var hostedDescriptors = services
                .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                .ToList();
            foreach (var d in hostedDescriptors)
                services.Remove(d);
        });
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // 验证真实 LLM Key（Integration 环境强制真实调用；DeepSeek/vLLM 均兼容 OpenAI 协议）
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(openAiKey))
            throw new InvalidOperationException(
                "Integration tests require OPENAI_API_KEY environment variable. " +
                "DeepSeek/vLLM 兼容 OpenAI 协议，通过 OPENAI_BASE_URL 指向对应端点即可。");

        if (File.Exists(DbPath))
            File.Delete(DbPath);

        // CreateClient 触发宿主构建；Program.cs 在 Integration 环境运行 DatabaseInitializer
        // （MigrateAsync + 基础种子）。
        // 关闭自动 cookie 处理：IntegrationHost.Api 是单例 HttpClient，若沿用默认 HandleCookies=true，
        // 任一响应写入的 ap_access_token cookie 会被后续「匿名」请求自动重放，导致鉴权泄漏
        // （匿名/越权场景误判为已认证）。认证统一由 AuthHelper 显式提取 JWT 经 WithBearer 附加。
        Api = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Integration 强制真实 LLM（gpt-4o-mini 或 OPENAI_BASE_URL 指向的 DeepSeek/vLLM 兼容端点）。
        // 真实端点首调用冷启动 / CI 网络抖动下，单条消息的完整回复常需 > 100s；HttpClient 默认
        // Timeout=100s 会把仍在进行的真实调用截断为 TaskCanceledException（客户端中止 → 服务端响应流
        // 中断 → 测试报 500/取消）。放宽到 5 分钟，给真实 LLM 完整回复留出余量（服务端 RouteAsync
        // 本身不设单次调用超时：RouterSettings.TimeoutSeconds 默认 0）。
        Api.Timeout = TimeSpan.FromMinutes(5);

        // 在基础种子之上追加集成专用数据（T2 用户 / T1·T2 ApiKey / 示例工作流）。
        await IntegrationSeeder.SeedAsync(Services);
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        // 尽力删除磁盘 SQLite 文件；若仍被连接占用（极端时序），忽略以免掩盖真实测试结果。
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch (IOException)
        {
        }
    }
}
