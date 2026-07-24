using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Workflow"/> aggregate, including the
/// owned legacy steps collection and the owned DAG (nodes + edges) collections.
/// </summary>
internal sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    /// <summary>
    /// Configures the entity type mapping for workflows.
    /// </summary>
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

        // AgentAssignments stores agent IDs (not full Agent aggregate); preserved for backward-compat.
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

        builder.OwnsMany(w => w.Nodes, nb =>
        {
            nb.WithOwner().HasForeignKey("WorkflowId");
            nb.Property<Guid>("Id").ValueGeneratedOnAdd();
            nb.HasKey("Id");
            nb.Property(n => n.Type)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            nb.Property(n => n.Name).IsRequired().HasMaxLength(200);
            nb.Property(n => n.Order).IsRequired();
            nb.Property(n => n.PositionX).IsRequired();
            nb.Property(n => n.PositionY).IsRequired();
            nb.Property(n => n.ConfigJson).HasMaxLength(16000).IsRequired();
            nb.Property(n => n.State)
                .HasConversion<string>()
                .HasMaxLength(50);
            nb.Property(n => n.Result).HasMaxLength(16000);
            nb.Property(n => n.ErrorDetail).HasMaxLength(8000);
        });

        builder.Navigation(w => w.Nodes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(w => w.Edges, eb =>
        {
            eb.WithOwner().HasForeignKey("WorkflowId");
            eb.Property<Guid>("Id").ValueGeneratedOnAdd();
            eb.HasKey("Id");
            eb.Property(e => e.SourceNodeId).IsRequired();
            eb.Property(e => e.TargetNodeId).IsRequired();
            eb.Property(e => e.Label).HasMaxLength(200);
        });

        builder.Navigation(w => w.Edges)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
