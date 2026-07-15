using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Conversation"/> aggregate.
/// </summary>
public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    /// <summary>
    /// Configures the entity type mapping for conversations, including table, primary key, tenant scoping, status conversion, owned token usage, and the owned messages collection.
    /// </summary>
    /// <param name="builder">The entity type builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.OwnsOne(c => c.TotalTokenUsage, t =>
        {
            t.Property(p => p.PromptTokens).HasColumnName("PromptTokens");
            t.Property(p => p.CompletionTokens).HasColumnName("CompletionTokens");
        });

        builder.OwnsMany(c => c.Messages, msg =>
        {
            msg.WithOwner().HasForeignKey("ConversationId");
            msg.Property<Guid>("Id").ValueGeneratedOnAdd();
            msg.HasKey("Id");
            msg.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(50);
            msg.Property(m => m.Content).IsRequired().HasMaxLength(16000);
            msg.Property(m => m.ToolCalls).HasMaxLength(8000);
            msg.OwnsOne(m => m.TokenUsage, t =>
            {
                t.Property(p => p.PromptTokens).HasColumnName("MsgPromptTokens");
                t.Property(p => p.CompletionTokens).HasColumnName("MsgCompletionTokens");
            });
        });

        builder.Navigation(c => c.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
