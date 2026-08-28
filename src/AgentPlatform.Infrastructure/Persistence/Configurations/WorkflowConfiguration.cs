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

        // F12 修复：IsDag（_isDag 私有字段）此前未持久化，导致「重跑已有工作流」经仓储重载后
        // IsDag 复位为 false，编排器误走遗留 Steps 投影（Type=null、ConfigJson="{}"）而非真实 DAG
        // Nodes，使 Code/Tool 等显式节点永不执行。现显式落库，重跑路径与创建即跑路径行为一致。
        builder.Property<bool>("_isDag")
            .HasColumnName("IsDag")
            .IsRequired()
            .HasDefaultValue(false);

        // AgentAssignments stores agent IDs (not full Agent aggregate); preserved for backward-compat.
        builder.Ignore(w => w.AgentAssignments);

        builder.OwnsMany(w => w.Steps, sb =>
        {
            sb.WithOwner().HasForeignKey("WorkflowId");
            // 关键：子实体主键由代码显式赋 Guid.NewGuid()，必须 ValueGeneratedNever，
            // 否则 EF 误判为"已存在实体"生成 UPDATE 而非 INSERT，导致 DbUpdateConcurrencyException。
            // 与 Message / KnowledgeDocument 的修复同因。
            sb.Property<Guid>("Id").ValueGeneratedNever();
            sb.HasKey("Id");
            sb.Property(s => s.StepName).IsRequired().HasMaxLength(200);
            sb.Property(s => s.State)
                .HasConversion<string>()
                .HasMaxLength(50);
            // 模型输出可能很长（真实 LLM 回复常超 16k），改用无长度上限的 text，
            // 避免 SaveChanges 时 String or binary data would be truncated → 500。
            sb.Property(s => s.Result).HasColumnType("text");
            // 错误分支的异常明细常超 8k，改用 text 避免截断 → 500。
            sb.Property(s => s.ErrorDetail).HasColumnType("text");
        });

        builder.Navigation(w => w.Steps)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(w => w.Nodes, nb =>
        {
            nb.WithOwner().HasForeignKey("WorkflowId");
            nb.Property<Guid>("Id").ValueGeneratedNever();
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
            nb.Property(n => n.Result).HasColumnType("text");
            // 错误分支的异常明细常超 8k，改用 text 避免截断 → 500。
            nb.Property(n => n.ErrorDetail).HasColumnType("text");
        });

        builder.Navigation(w => w.Nodes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(w => w.Edges, eb =>
        {
            eb.WithOwner().HasForeignKey("WorkflowId");
            eb.Property<Guid>("Id").ValueGeneratedNever();
            eb.HasKey("Id");
            eb.Property(e => e.SourceNodeId).IsRequired();
            eb.Property(e => e.TargetNodeId).IsRequired();
            eb.Property(e => e.Label).HasMaxLength(200);
        });

        builder.Navigation(w => w.Edges)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
