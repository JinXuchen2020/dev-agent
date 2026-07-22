using AgentPlatform.Domain.Aggregates.ApiKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="ApiKey"/> aggregate.
/// Maps to the ApiKeys table with encrypted key storage and tenant isolation.
/// </summary>
internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.TenantId).IsRequired();
        builder.Property(k => k.EncryptedKeyHash).IsRequired().HasMaxLength(500);
        builder.Property(k => k.KeyPrefix).IsRequired().HasMaxLength(20);
        builder.Property(k => k.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(k => k.RolesCsv).HasMaxLength(500);
        builder.Property(k => k.IsActive).IsRequired();
        builder.Property(k => k.KeyVersion).IsRequired().HasDefaultValue(1);
        builder.Property(k => k.CreatedAt).IsRequired();
        builder.Property(k => k.ExpiresAt);
        builder.Property(k => k.RevokedAt);
        builder.Ignore(k => k.DomainEvents);
        builder.HasIndex(k => new { k.TenantId, k.IsActive });
        builder.HasIndex(k => new { k.IsActive, k.RevokedAt, k.ExpiresAt });
    }
}
