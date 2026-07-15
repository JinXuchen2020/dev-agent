using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// 数据库初始化服务实现，负责数据库迁移、表创建和种子数据填充。
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly AppDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        AppDbContext context,
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing database...");

            // 显式创建数据库和表
            var created = await _context.Database.EnsureCreatedAsync();
            if (created)
            {
                _logger.LogInformation("Database created for the first time.");
            }
            else
            {
                _logger.LogInformation("Database already exists.");
            }

            // 初始化种子数据
            await SeedDataAsync();

            // 保存所有更改
            var saved = await _context.SaveChangesAsync();
            _logger.LogInformation("Database initialization completed with {Count} entities saved.", saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    private async Task SeedDataAsync()
    {
        try
        {
            _logger.LogInformation("Seeding initial data...");

            // 检查是否已经有数据
            var roleCount = await _context.AgentRoleDefinitions.CountAsync();
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

            await _context.AgentRoleDefinitions.AddRangeAsync(roles);
            _logger.LogInformation("Added {Count} agent role definitions to context, saving...", roles.Count);
            
            // 调用 SaveChangesAsync 保存到数据库
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully seeded {Count} agent role definitions", roles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed data, but continuing with startup");
            _logger.LogWarning("This may cause issues when querying tables. Please check the database initialization.");
        }
    }
}
