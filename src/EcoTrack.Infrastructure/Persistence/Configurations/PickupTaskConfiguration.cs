using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class PickupTaskConfiguration : IEntityTypeConfiguration<PickupTask>
{
    public void Configure(EntityTypeBuilder<PickupTask> builder)
    {
        builder.ToTable("PickupTasks");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PickupCode).IsRequired().HasMaxLength(32);
        builder.HasIndex(p => p.PickupCode).IsUnique();

        builder.Property(p => p.SiteName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.SiteAddressText).IsRequired().HasMaxLength(500);
        builder.Property(p => p.ScheduledAtUtc).IsRequired();
        builder.Property(p => p.EstimatedWeightKg).HasPrecision(18, 3).IsRequired();
        builder.Property(p => p.CollectedWeightKg).HasPrecision(18, 3);
        builder.Property(p => p.Status).HasConversion<string>().IsRequired();
        builder.Property(p => p.AssignedCollectorUserId);
        builder.Property(p => p.AssignedAtUtc);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.CreatedByUserId).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        builder.Property(p => p.CancelledByUserId);
        builder.Property(p => p.CancelledAtUtc);
        builder.Property(p => p.CancelReason).HasMaxLength(1000);

        builder.HasMany(p => p.AssignmentEvents)
            .WithOne()
            .HasForeignKey(a => a.PickupTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
