using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class RecyclingBatchConfiguration : IEntityTypeConfiguration<RecyclingBatch>
{
    public void Configure(EntityTypeBuilder<RecyclingBatch> builder)
    {
        builder.ToTable("RecyclingBatches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SegregationBatchId).IsRequired();
        builder.Property(x => x.PickupTaskId).IsRequired();
        builder.Property(x => x.SourceCategory).IsRequired().HasMaxLength(20);
        builder.Property(x => x.SourceWeightKg).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.Stage).HasConversion<string>().IsRequired();
        builder.Property(x => x.OutputProduct).HasMaxLength(255);
        builder.Property(x => x.OutputQuantity).HasPrecision(18, 3);
        builder.Property(x => x.InventoryUpdated).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.UpdatedByUserId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.OwnsMany(x => x.StageHistory, a =>
        {
            a.ToTable("RecyclingBatchStageHistory");
            a.WithOwner().HasForeignKey("RecyclingBatchId");
            a.HasKey("Id");
            a.Property(p => p.Stage).HasConversion<string>().IsRequired();
            a.Property(p => p.AtUtc).IsRequired();
            a.Property(p => p.ByUserId).IsRequired();
        });

        builder.HasIndex(x => x.SegregationBatchId);
        builder.HasIndex(x => x.PickupTaskId);
        builder.HasIndex(x => x.Stage);
    }
}
