using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class PickupAssignmentEventConfiguration : IEntityTypeConfiguration<PickupAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<PickupAssignmentEvent> builder)
    {
        builder.ToTable("PickupAssignmentEvents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.PickupTaskId).IsRequired();
        builder.Property(a => a.PreviousCollectorUserId);
        builder.Property(a => a.NewCollectorUserId).IsRequired();
        builder.Property(a => a.ChangedByUserId).IsRequired();
        builder.Property(a => a.ChangedAtUtc).IsRequired();
        builder.Property(a => a.Note).HasMaxLength(1000);
    }
}
