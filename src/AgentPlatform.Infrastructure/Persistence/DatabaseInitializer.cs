using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// 数据库初始化服务实现，负责数据库迁移、表创建和种子数据填充。
/// </summary>
internal sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly AppDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly TenantSettings _tenantSettings;

    // Default tenant GUID used when no tenant is configured — all-zeros is explicit sentinel.
    // Configure via Tenant:DefaultTenantId in appsettings or user-secrets.
    private static readonly Guid DefaultTenantIdSeed = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public DatabaseInitializer(
        AppDbContext context,
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializer> logger,
        IOptions<TenantSettings> tenantSettings)
    {
        _context = context;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tenantSettings = tenantSettings.Value;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Initializing database...");

            // Apply EF Core migrations — the single source of truth for the schema.
            // Unlike EnsureCreatedAsync (which only creates tables when the database file
            // does not yet exist), MigrateAsync also upgrades an existing database when new
            // migrations are added. This is required so tables introduced by later migrations
            // (e.g. AgentConfigurations, ApiKeys, AuditLogs) get created for databases that were
            // first created before those migrations existed — otherwise queries fail with
            // "no such table".
            await ApplyMigrationsAsync(ct);

            // 初始化种子数据
            await SeedDataAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    private async Task ApplyMigrationsAsync(CancellationToken ct)
    {
        try
        {
            var pending = await _context.Database.GetPendingMigrationsAsync(ct);
            if (pending.Any())
            {
                _logger.LogInformation(
                    "Applying {Count} pending migration(s): {Migrations}",
                    pending.Count(),
                    string.Join(", ", pending));
                await _context.Database.MigrateAsync(ct);
                _logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                _logger.LogInformation("Database is up to date — no pending migrations.");
            }
        }
        catch (Exception ex)
        {
            // Fallback for providers that do not support migrations (e.g. the EF Core
            // InMemory provider used by some tests). EnsureCreated builds the schema from
            // the current model.
            _logger.LogWarning(ex, "MigrateAsync failed; falling back to EnsureCreated for non-migration providers.");
            await _context.Database.EnsureCreatedAsync(ct);
        }
    }

    private async Task SeedDataAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Seeding initial data...");

            // 检查是否已经有数据
            var roleCount = await _context.AgentRoleDefinitions.CountAsync(ct);
            if (roleCount > 0)
            {
                _logger.LogInformation("Database already contains {Count} agent role definitions, skipping seed", roleCount);
                return;
            }

            // 创建种子 Agent 角色定义数据
            var roles = new List<Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition>
            {
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "需求分析师",
                    "requirement",
                    "负责收集、分析和整理业务需求",
                    "你是一个专业的需求分析师，擅长收集、分析和整理业务需求..."
                ),
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "产品经理",
                    "product",
                    "负责产品规划、功能设计和路线图制定",
                    "你是一个经验丰富的产品经理，擅长产品规划和功能设计..."
                ),
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "系统架构",
                    "architecture",
                    "负责系统架构设计和技术选型",
                    "你是一个资深系统架构师，擅长系统架构设计和技术选型..."
                ),
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "代码实现",
                    "development",
                    "负责功能开发和代码实现",
                    "你是一个经验丰富的开发工程师，擅长功能开发和代码实现..."
                ),
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "质量保证",
                    "testing",
                    "负责功能测试和质量保证",
                    "你是一个专业的测试工程师，擅长功能测试和质量保证..."
                ),
                new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                    Guid.NewGuid(),
                    "文档编写",
                    "documentation",
                    "负责技术文档和用户文档编写",
                    "你是一个专业的文档工程师，擅长技术文档和用户文档编写..."
                )
            };

            await _context.AgentRoleDefinitions.AddRangeAsync(roles, ct);
            _logger.LogInformation("Added {Count} agent role definitions to context, saving...", roles.Count);
            
            // 调用 SaveChangesAsync 保存到数据库
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully seeded {Count} agent role definitions", roles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed data, but continuing with startup");
            _logger.LogWarning("This may cause issues when querying tables. Please check the database initialization.");
        }

        // Seed default agent configurations
        try
        {
            var configCount = await _context.AgentConfigurations.CountAsync(ct);
            if (configCount > 0)
            {
                _logger.LogInformation("Database already contains {Count} agent configurations, skipping seed", configCount);
                return;
            }

            var defaultTenantId = _tenantSettings.DefaultTenantId != Guid.Empty
                ? _tenantSettings.DefaultTenantId
                : DefaultTenantIdSeed;
            var configurations = new List<AgentConfiguration>
            {
                new(
                    Guid.NewGuid(),
                    "Default Requirement Agent",
                    "name: requirement-agent\nsystem_prompt: \"You are a professional requirements analyst...\"\nmodel: deepseek-chat\ntemperature: 0.3",
                    defaultTenantId,
                    version: ConfigurationVersion.Initial,
                    description: "Default configuration for the requirements analyst agent role",
                    agentTypeCode: "requirement"),
                new(
                    Guid.NewGuid(),
                    "Default Development Agent",
                    "name: development-agent\nsystem_prompt: \"You are an experienced software engineer...\"\nmodel: deepseek-chat\ntemperature: 0.5",
                    defaultTenantId,
                    version: ConfigurationVersion.Initial,
                    description: "Default configuration for the development agent role",
                    agentTypeCode: "development"),
                new(
                    Guid.NewGuid(),
                    "Default Architecture Agent",
                    "name: architecture-agent\nsystem_prompt: \"You are a senior system architect...\"\nmodel: deepseek-chat\ntemperature: 0.4",
                    defaultTenantId,
                    version: ConfigurationVersion.Initial,
                    description: "Default configuration for the architecture agent role",
                    agentTypeCode: "architecture"),
            };

            await _context.AgentConfigurations.AddRangeAsync(configurations, ct);
            _logger.LogInformation("Added {Count} default agent configurations to context, saving...", configurations.Count);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully seeded {Count} default agent configurations", configurations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed data, but continuing with startup");
            _logger.LogWarning("This may cause issues when querying tables. Please check the database initialization.");
        }
    }
}
