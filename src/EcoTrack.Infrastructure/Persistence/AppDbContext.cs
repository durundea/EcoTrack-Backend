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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
