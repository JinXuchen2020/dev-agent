using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Workflow"/> aggregate.
/// </summary>
public sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    /// <summary>
    /// Configures the entity type mapping for workflows, including table, primary key, property constraints, state conversion, ignored runtime mappings, and the owned steps collection.
    /// </summary>
    /// <param name="builder">The entity type builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("Workflows");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.CurrentState)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(w => w.Context).HasMaxLength(16000);
        builder.Property(w => w.TenantId).IsRequired();

        // AgentAssignments stores agent IDs (not full Agent aggregate), replaced by a join table in phase 2
        builder.Ignore(w => w.AgentAssignments);

        builder.OwnsMany(w => w.Steps, sb =>
        {
            sb.WithOwner().HasForeignKey("WorkflowId");
            sb.Property<Guid>("Id").ValueGeneratedOnAdd();
            sb.HasKey("Id");
            sb.Property(s => s.StepName).IsRequired().HasMaxLength(200);
            sb.Property(s => s.State)
                .HasConversion<string>()
                .HasMaxLength(50);
            sb.Property(s => s.Result).HasMaxLength(16000);
            sb.Property(s => s.ErrorDetail).HasMaxLength(8000);
        });

        builder.Navigation(w => w.Steps)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
