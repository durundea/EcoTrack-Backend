using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class ProductConversionConfiguration : IEntityTypeConfiguration<ProductConversion>
{
    public void Configure(EntityTypeBuilder<ProductConversion> builder)
    {
        builder.ToTable("ProductConversions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RecyclingBatchId).IsRequired();
        builder.Property(x => x.ProductName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.Unit).IsRequired().HasMaxLength(20);
        builder.Property(x => x.SyncedAtUtc);
        builder.Property(x => x.SyncRunId).HasMaxLength(100);
        builder.Property(x => x.SyncedByUserId);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.RecyclingBatchId);
        builder.HasIndex(x => x.SyncedAtUtc);
    }
}
