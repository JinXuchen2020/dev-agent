using AgentPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using DomainAgentConfiguration = AgentPlatform.Domain.Aggregates.AgentConfigurations.AgentConfiguration;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="DomainAgentConfiguration"/> aggregate.
/// </summary>
internal sealed class AgentConfigurationConfiguration : IEntityTypeConfiguration<DomainAgentConfiguration>
{
    public void Configure(EntityTypeBuilder<DomainAgentConfiguration> builder)
    {
        builder.ToTable("AgentConfigurations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.YamlContent)
            .IsRequired()
            .HasColumnType("text");

        builder.OwnsOne(x => x.Version, v =>
        {
            v.Property(p => p.Major).HasColumnName("VersionMajor").IsRequired();
            v.Property(p => p.Minor).HasColumnName("VersionMinor").IsRequired();
            v.Property(p => p.Patch).HasColumnName("VersionPatch").IsRequired();
            v.Property(p => p.ChangeLog).HasColumnName("VersionChangeLog").HasMaxLength(2000);
        });

        builder.Property(x => x.AgentTypeCode)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasConversion(new EnumToStringConverter<AgentConfigurationStatus>())
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.AgentTypeCode);
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
