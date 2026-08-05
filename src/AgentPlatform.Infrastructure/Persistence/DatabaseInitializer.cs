using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _environment;

    // Default tenant GUID used when no tenant is configured — all-zeros is explicit sentinel.
    // Configure via Tenant:DefaultTenantId in appsettings or user-secrets.
    private static readonly Guid DefaultTenantIdSeed = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // 集成测试固定夹具：仅 Integration 环境播种，供前端 E2E（Playwright）与手动集成验证复用。
    // 明文为 dev-only 固定值，与前端 e2e/publish-workflow.spec.ts 约定一致，绝不用于生产。
    private static readonly Guid IntegrationApiKeyId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid IntegrationWorkflowId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    private const string IntegrationApiKeyPlaintext = "integration-fixture-key-0001";
    private const string IntegrationWorkflowName = "Integration Fixture Workflow";

    public DatabaseInitializer(
        AppDbContext context,
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializer> logger,
        IOptions<TenantSettings> tenantSettings,
        IHostEnvironment environment)
    {
        _context = context;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tenantSettings = tenantSettings.Value;
        _environment = environment;
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

            // 集成测试夹具（仅 Integration 环境）：供前端 E2E 与手动集成验证复用。
            if (_environment.IsEnvironment("Integration"))
                await SeedIntegrationFixturesAsync(ct);
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

            // Seed a default admin user for email + password login.
            // Idempotent: only seeds when the Users table is empty.
            try
            {
                var userCount = await _context.Users.CountAsync(ct);
                if (userCount == 0)
                {
                    var hasher = _serviceProvider.GetRequiredService<IPasswordHasher>();
                    var defaultTenantId = _tenantSettings.DefaultTenantId != Guid.Empty
                        ? _tenantSettings.DefaultTenantId
                        : DefaultTenantIdSeed;
                    const string defaultPassword = "Admin@123456";
                    var admin = new User(
                        Guid.NewGuid(),
                        defaultTenantId,
                        "admin@acme.io",
                        hasher.Hash(defaultPassword),
                        "Admin");
                    _context.Users.Add(admin);
                    await _context.SaveChangesAsync(ct);
                    _logger.LogWarning(
                        "Seeded default admin user admin@acme.io (password: {Password}). CHANGE THIS IN PRODUCTION.",
                        defaultPassword);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed default user, but continuing with startup");
            }

            // 创建 / 对齐种子 Agent 角色定义（内建角色目录，DB 为准）。
            // 既有数据库可能已在 IsBuiltIn 列存在前被种子化 —— 此处做幂等对齐：
            //   · 缺失的内建 code → 插入并标记 IsBuiltIn=true
            //   · 已存在但 IsBuiltIn=false 的内建 code → 补标记（不覆盖既有 Name/Description/SystemPrompt）
            var builtInSeed = new Dictionary<string, (string Name, string Description, string SystemPrompt)>
            {
                [BuiltInRoleCatalog.Requirement] = ("需求分析师", "负责收集、分析和整理业务需求", "你是一个专业的需求分析师，擅长收集、分析和整理业务需求..."),
                [BuiltInRoleCatalog.Product] = ("产品经理", "负责产品规划、功能设计和路线图制定", "你是一个经验丰富的产品经理，擅长产品规划和功能设计..."),
                [BuiltInRoleCatalog.Architecture] = ("系统架构", "负责系统架构设计和技术选型", "你是一个资深系统架构师，擅长系统架构设计和技术选型..."),
                [BuiltInRoleCatalog.Development] = ("代码实现", "负责功能开发和代码实现", "你是一个经验丰富的开发工程师，擅长功能开发和代码实现..."),
                [BuiltInRoleCatalog.Testing] = ("质量保证", "负责功能测试和质量保证", "你是一个专业的测试工程师，擅长功能测试和质量保证..."),
                [BuiltInRoleCatalog.Documentation] = ("文档编写", "负责技术文档和用户文档编写", "你是一个专业的文档工程师，擅长技术文档和用户文档编写..."),
                [BuiltInRoleCatalog.Reviewer] = ("评审专家", "负责代码与设计评审", "你是一个严谨的评审专家，擅长审查代码质量和架构设计合理性..."),
            };

            var existingRoles = await _context.AgentRoleDefinitions.ToListAsync(ct);
            foreach (var (code, (name, description, systemPrompt)) in builtInSeed)
            {
                var existing = existingRoles.FirstOrDefault(r => r.RoleCode == code);
                if (existing is null)
                {
                    _context.AgentRoleDefinitions.Add(new Domain.Aggregates.AgentRoleDefinitions.AgentRoleDefinition(
                        Guid.NewGuid(), name, code, description, systemPrompt, isBuiltIn: true));
                }
                else if (!existing.IsBuiltIn)
                {
                    existing.MarkAsBuiltIn();
                }
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Reconciled {Count} built-in agent role definitions", builtInSeed.Count);

            // 兼容历史数据：F19 将内建角色 code 由旧值统一为 BuiltInRoleCatalog 新值
            // （architect→architecture / developer→development / tester→testing /
            // pm→product / tech-writer→documentation；reviewer 保持不变）。
            // 存量 Agent 的 RoleCode 做一次幂等映射，避免其游离于新目录之外
            // （旧 code 不再出现在 BuiltInRoleCatalog，会导致引用计数与编辑下拉无法匹配）。
            var legacyToNew = new Dictionary<string, string>
            {
                ["architect"] = BuiltInRoleCatalog.Architecture,
                ["developer"] = BuiltInRoleCatalog.Development,
                ["tester"] = BuiltInRoleCatalog.Testing,
                ["pm"] = BuiltInRoleCatalog.Product,
                ["tech-writer"] = BuiltInRoleCatalog.Documentation,
            };
            var legacyCodes = legacyToNew.Keys.ToList();
            var orphanAgents = await _context.Agents
                .IgnoreQueryFilters()
                .Where(a => legacyCodes.Contains(a.Role.RoleCode))
                .ToListAsync(ct);
            foreach (var agent in orphanAgents)
            {
                var newCode = legacyToNew[agent.Role.RoleCode];
                var newType = AgentType.FromCode(newCode);
                if (newType is not null)
                    agent.UpdateRole(newType);
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Remapped {Count} legacy agent role codes", orphanAgents.Count);
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

    /// <summary>
    /// 集成测试夹具：仅在 <c>Integration</c> 环境播种，供前端 E2E（Playwright）与手动集成验证复用。
    /// 播种一个已知明文 ApiKey 与一个 Completed 示例工作流，与后端 BDD（Reqnroll）的
    /// <c>IntegrationSeeder</c> 对齐理念一致，但落于生产 Infrastructure 以避免测试工程耦合。
    /// 幂等：依赖固定 Guid 主键 + <c>IgnoreQueryFilters</c> 判重。
    /// </summary>
    private async Task SeedIntegrationFixturesAsync(CancellationToken ct = default)
    {
        try
        {
            var defaultTenantId = _tenantSettings.DefaultTenantId != Guid.Empty
                ? _tenantSettings.DefaultTenantId
                : DefaultTenantIdSeed;

            // ── 已知明文 ApiKey ──
            if (await _context.ApiKeys.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(k => k.Id == IntegrationApiKeyId, ct) is null)
            {
                var encryption = _serviceProvider.GetRequiredService<IApiKeyEncryptionService>();
                var (encrypted, prefix) = encryption.EncryptKey(IntegrationApiKeyPlaintext);
                _context.ApiKeys.Add(new ApiKey(
                    IntegrationApiKeyId,
                    defaultTenantId,
                    encrypted,
                    prefix,
                    "Integration Fixture Key",
                    "Admin",
                    null));
                _logger.LogInformation("Seeded integration fixture ApiKey (plaintext dev-only).");
            }

            // ── Completed 示例工作流（发布 / 运行场景）──
            if (await _context.Workflows.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == IntegrationWorkflowId, ct) is null)
            {
                var wf = new Workflow(IntegrationWorkflowId, IntegrationWorkflowName, defaultTenantId);
                wf.ReplaceSteps(new[] { "Generate content" });
                wf.Complete();
                _context.Workflows.Add(wf);
                _logger.LogInformation("Seeded integration fixture workflow '{Name}'.", IntegrationWorkflowName);
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed integration fixtures, but continuing with startup");
        }
    }
}
