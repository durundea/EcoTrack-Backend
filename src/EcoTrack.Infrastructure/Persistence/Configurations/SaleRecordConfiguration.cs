using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class SaleRecordConfiguration : IEntityTypeConfiguration<SaleRecord>
{
    public void Configure(EntityTypeBuilder<SaleRecord> builder)
    {
        builder.ToTable("SaleRecords");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.InventoryItemId).IsRequired();
        builder.Property(s => s.RequestedByUserId).IsRequired();
        builder.Property(s => s.QuantitySold).IsRequired();
        builder.Property(s => s.RevenueInr).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.SoldAtUtc).IsRequired();
        builder.Property(s => s.ApprovalStatus).HasConversion<string>().IsRequired();
        builder.Property(s => s.RejectionReason).HasMaxLength(1000);
        builder.Property(s => s.ApprovedByUserId);
        builder.Property(s => s.ApprovedAtUtc);
        builder.Property(s => s.RejectedByUserId);
        builder.Property(s => s.RejectedAtUtc);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.Ignore(s => s.CanBeModified);

        builder.HasOne<EcoTrack.Domain.Inventory.InventoryItem>()
            .WithMany()
            .HasForeignKey(s => s.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EcoTrack.Domain.Auth.User>()
            .WithMany()
            .HasForeignKey(s => s.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
