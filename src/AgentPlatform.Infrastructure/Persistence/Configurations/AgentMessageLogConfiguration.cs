using AgentPlatform.Domain.Aggregates.AgentMessages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="AgentMessageLog"/> durable message log (F32).
/// </summary>
internal sealed class AgentMessageLogConfiguration : IEntityTypeConfiguration<AgentMessageLog>
{
    public void Configure(EntityTypeBuilder<AgentMessageLog> builder)
    {
        builder.ToTable("AgentMessageLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // = MessageId supplied by the bus

        builder.Property(x => x.WorkflowId).IsRequired();
        builder.Property(x => x.CorrelationId).IsRequired();
        builder.Property(x => x.SenderId).IsRequired();
        builder.Property(x => x.ReceiverId).IsRequired();

        builder.Property(x => x.MessageType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Payload)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Round).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ConsumedAt);

        // Scheduler/redelivery scan + trace replay
        builder.HasIndex(x => new { x.TenantId, x.WorkflowId });
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => new { x.WorkflowId, x.ConsumedAt });
    }
}