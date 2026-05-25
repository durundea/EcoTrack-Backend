using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Category).HasConversion<string>().IsRequired();
        builder.Property(i => i.QuantityKg).HasPrecision(18, 4).IsRequired();
        builder.Property(i => i.Unit).IsRequired().HasMaxLength(50);
        builder.Property(i => i.StandardPriceInr).HasPrecision(18, 4).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();
    }
}
