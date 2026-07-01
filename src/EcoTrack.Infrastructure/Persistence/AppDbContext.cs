using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Infrastructure.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<SaleRecord> SaleRecords => Set<SaleRecord>();
    public DbSet<PickupTask> PickupTasks => Set<PickupTask>();
    public DbSet<PickupAssignmentEvent> PickupAssignmentEvents => Set<PickupAssignmentEvent>();
    public DbSet<SegregationBatch> SegregationBatches => Set<SegregationBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
