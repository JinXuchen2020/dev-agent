using AgentPlatform.Domain.Aggregates.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="WorkflowVersion"/> aggregate.
/// Key is set explicitly in code, so <c>ValueGeneratedNever</c> avoids the UPDATE-vs-INSERT trap.
/// </summary>
internal sealed class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    /// <summary>Configures the entity type mapping for workflow versions.</summary>
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.ToTable("WorkflowVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.WorkflowId).IsRequired();
        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        // SnapshotJson holds a serialized graph; leave unbounded (nvarchar max).
        builder.Property(v => v.SnapshotJson).IsRequired();
        builder.Property(v => v.Note).HasMaxLength(2000);
        builder.Property(v => v.CreatedBy);
        builder.Property(v => v.CreatedAt).IsRequired();
        builder.HasIndex(v => new { v.WorkflowId, v.VersionNumber });
    }
}
