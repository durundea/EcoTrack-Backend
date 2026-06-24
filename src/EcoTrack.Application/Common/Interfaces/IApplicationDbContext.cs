using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<SaleRecord> SaleRecords { get; }
    DbSet<PickupTask> PickupTasks { get; }
    DbSet<PickupAssignmentEvent> PickupAssignmentEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
