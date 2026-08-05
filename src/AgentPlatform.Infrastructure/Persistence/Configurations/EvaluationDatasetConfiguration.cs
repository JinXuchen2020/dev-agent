using AgentPlatform.Domain.Aggregates.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="EvaluationDataset"/> aggregate root and its
/// owned collection of <see cref="EvaluationCase"/> items (F24).
/// </summary>
internal sealed class EvaluationDatasetConfiguration : IEntityTypeConfiguration<EvaluationDataset>
{
    public void Configure(EntityTypeBuilder<EvaluationDataset> builder)
    {
        builder.ToTable("EvaluationDatasets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        // Owned collection: EvaluationCase
        builder.Navigation(x => x.Cases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(x => x.Cases, caseBuilder =>
        {
            caseBuilder.ToTable("EvaluationCases");
            caseBuilder.WithOwner().HasForeignKey("EvaluationDatasetId");

            caseBuilder.HasKey(x => x.Id);

            // EF convention mis-flags a pre-set Guid key as ValueGeneratedOnAdd, which
            // produces UPDATE instead of INSERT for owned children -> concurrency error.
            caseBuilder.Property(x => x.Id)
                .ValueGeneratedNever();

            caseBuilder.Property(x => x.Input)
                .IsRequired()
                .HasMaxLength(4000);

            caseBuilder.Property(x => x.ExpectedOutput)
                .IsRequired()
                .HasMaxLength(4000);

            caseBuilder.Property(x => x.MatchMode)
                .IsRequired()
                .HasConversion<int>();
        });

        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}
