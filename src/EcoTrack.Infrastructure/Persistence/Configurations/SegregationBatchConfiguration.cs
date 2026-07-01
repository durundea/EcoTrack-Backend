using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class SegregationBatchConfiguration : IEntityTypeConfiguration<SegregationBatch>
{
    public void Configure(EntityTypeBuilder<SegregationBatch> builder)
    {
        builder.ToTable("SegregationBatches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PickupTaskId).IsRequired();
        builder.Property(x => x.BatchCode).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired();

        builder.Property(x => x.PlasticKg).HasPrecision(18, 3);
        builder.Property(x => x.OrganicKg).HasPrecision(18, 3);
        builder.Property(x => x.MetalKg).HasPrecision(18, 3);
        builder.Property(x => x.PaperKg).HasPrecision(18, 3);
        builder.Property(x => x.EWasteKg).HasPrecision(18, 3);

        builder.Property(x => x.RecordedByUserId);
        builder.Property(x => x.RecordedAtUtc);
        builder.Property(x => x.RecycledByUserId);
        builder.Property(x => x.RecycledAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.BatchCode).IsUnique();
        builder.HasIndex(x => x.PickupTaskId).IsUnique();

        builder.HasOne<PickupTask>()
            .WithMany()
            .HasForeignKey(x => x.PickupTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
