using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="AgentRoleDefinition"/> aggregate.
/// </summary>
internal sealed class AgentRoleDefinitionConfiguration : IEntityTypeConfiguration<AgentRoleDefinition>
{
    public void Configure(EntityTypeBuilder<AgentRoleDefinition> builder)
    {
        builder.ToTable("AgentRoleDefinitions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.RoleCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.SystemPrompt)
            .IsRequired()
            .HasMaxLength(8000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.RoleCode)
            .IsUnique();
    }
}
