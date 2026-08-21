using AgentPlatform.Domain.Aggregates.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Agent"/> aggregate.
/// </summary>
internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    /// <summary>
    /// Configures the entity type mapping for agents, including table, primary key, property constraints, owned model endpoint, and ignored navigation collections.
    /// </summary>
    /// <param name="builder">The entity type builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.OwnsOne(a => a.Role, o =>
        {
            o.Property(p => p.RoleCode).HasColumnName("RoleCode").IsRequired().HasMaxLength(50);
            o.Property(p => p.DisplayName).HasColumnName("RoleDisplayName").IsRequired().HasMaxLength(100);
            o.Property(p => p.Description).HasColumnName("RoleDescription").HasMaxLength(500);
        });
        builder.Property(a => a.SystemPrompt).HasMaxLength(8000);
        builder.Property(a => a.TenantId).IsRequired();
        builder.OwnsOne(a => a.ModelEndpoint, m =>
        {
            m.Property(e => e.Provider).HasColumnName("ModelProvider").HasMaxLength(50);
            m.Property(e => e.ModelName).HasColumnName("ModelName").HasMaxLength(100);
            m.Property(e => e.ApiUrl).HasColumnName("ModelApiUrl").HasMaxLength(500);
            m.Property(e => e.MaxTokens).HasColumnName("ModelMaxTokens");
            m.Property(e => e.Temperature).HasColumnName("ModelTemperature");
        });
        builder.Ignore(a => a.Tools);
        builder.Ignore(a => a.SkillPackages);
        builder.Ignore(a => a.McpServers);
        // ── F29 Agentic Agent Primitive：自主控制循环的可配置项 ──
        builder.Property(a => a.AllowedToolNamesJson).HasColumnName("AllowedToolNamesJson").HasDefaultValue("[]").IsRequired();
        builder.Property(a => a.MaxIterations).HasColumnName("MaxIterations").HasDefaultValue(25).IsRequired();
        builder.Property(a => a.StopCriteria).HasColumnName("StopCriteria").HasMaxLength(500);
    }
}
