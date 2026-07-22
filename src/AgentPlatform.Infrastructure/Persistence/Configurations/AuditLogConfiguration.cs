using AgentPlatform.Domain.Aggregates.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(100);
        builder.Property(x => x.Action)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.Entity).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => x.Action);
    }
}
