using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="ToolDefinition"/> aggregate.
/// </summary>
public sealed class ToolDefinitionConfiguration : IEntityTypeConfiguration<ToolDefinition>
{
    /// <summary>
    /// Configures the entity type mapping for tool definitions, including table, primary key, property constraints, and source conversion.
    /// </summary>
    /// <param name="builder">The entity type builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<ToolDefinition> builder)
    {
        builder.ToTable("ToolDefinitions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(1000);
        builder.Property(t => t.ParametersSchema).IsRequired();
        builder.Property(t => t.HandlerName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.IsEnabled);
        builder.Property(t => t.Source)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(t => t.EndpointUrl).HasMaxLength(500);
        builder.Property(t => t.SkillPluginName).HasMaxLength(200);
        builder.Ignore(t => t.DomainEvents);
    }
}
