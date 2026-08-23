using AgentPlatform.Domain.Aggregates.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="AgentRunRecord"/> aggregate.
/// </summary>
internal sealed class AgentRunRecordConfiguration : IEntityTypeConfiguration<AgentRunRecord>
{
    public void Configure(EntityTypeBuilder<AgentRunRecord> builder)
    {
        builder.ToTable("AgentRunRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.AgentName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.RunId).IsRequired();
        builder.Property(x => x.Goal)
            .IsRequired()
            .HasColumnType("text");
        builder.Property(x => x.FinalAnswer)
            .HasColumnType("text");
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.AgentId, x.CreatedAt });
        builder.HasIndex(x => x.RunId).IsUnique();
        builder.HasIndex(x => x.TenantId);
    }
}
