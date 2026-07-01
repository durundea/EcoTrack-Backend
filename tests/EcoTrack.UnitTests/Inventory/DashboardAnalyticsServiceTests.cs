using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.UnitTests.Inventory;

public class DashboardAnalyticsServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_WhenCollectedDenominatorIsZero_ReturnsZeroRecyclingEfficiency()
    {
        await using var db = CreateDbContext();

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 0.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, "all"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        result.Kpis.RecyclingEfficiencyPercent.Should().Be(0m);
    }

    [Fact]
    public async Task GetDashboardAsync_AppliesConfiguredCo2FactorByCategory()
    {
        await using var db = CreateDbContext();

        var raw = InventoryItem.Create("Raw", InventoryCategory.RawWaste, 100m, "kg", 10m, DateTime.UtcNow);
        var recycled = InventoryItem.Create("Recycled", InventoryCategory.RecycledProduct, 100m, "kg", 12m, DateTime.UtcNow);
        db.InventoryItems.AddRange(raw, recycled);

        var rawSale = CreatePendingSale(raw.Id, 10, 100m);
        var recycledSale = CreateApprovedSale(recycled.Id, 10, 100m);
        db.SaleRecords.AddRange(rawSale, recycledSale);

        await db.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 0.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(2), "all"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        result.Kpis.Co2ReductionKg.Should().Be(13.0m);
    }

    [Fact]
    public async Task GetDashboardAsync_ComputesCategorySharePercentages()
    {
        await using var db = CreateDbContext();

        var raw = InventoryItem.Create("Raw", InventoryCategory.RawWaste, 100m, "kg", 10m, DateTime.UtcNow);
        var recycled = InventoryItem.Create("Recycled", InventoryCategory.RecycledProduct, 100m, "kg", 12m, DateTime.UtcNow);
        db.InventoryItems.AddRange(raw, recycled);

        var rawSale = CreatePendingSale(raw.Id, 3, 30m);
        var recycledSale = CreatePendingSale(recycled.Id, 1, 10m);
        db.SaleRecords.AddRange(rawSale, recycledSale);

        await db.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 0.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(2), "all"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        var rawRow = result.WasteByCategory.Single(x => x.Category == "RawWaste");
        var recycledRow = result.WasteByCategory.Single(x => x.Category == "RecycledProduct");

        rawRow.SharePercent.Should().Be(75.0m);
        recycledRow.SharePercent.Should().Be(25.0m);
    }

    private static DashboardAnalyticsService CreateService(
        TestDbContext db,
        Dictionary<string, decimal> factors)
    {
        var options = Options.Create(new DashboardAnalyticsOptions
        {
            Co2FactorsKgPerKgByCategory = factors
        });

        return new DashboardAnalyticsService(db, options);
    }

    private static SaleRecord CreatePendingSale(Guid inventoryItemId, int quantitySold, decimal revenueInr)
    {
        var ownerId = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(inventoryItemId, ownerId, quantitySold, revenueInr, DateTime.UtcNow, DateTime.UtcNow);
        sale.SubmitForApproval(ownerId, UserRole.Admin, DateTime.UtcNow);
        return sale;
    }

    private static SaleRecord CreateApprovedSale(Guid inventoryItemId, int quantitySold, decimal revenueInr)
    {
        var sale = CreatePendingSale(inventoryItemId, quantitySold, revenueInr);
        sale.Approve(Guid.NewGuid(), UserRole.Admin, DateTime.UtcNow);
        return sale;
    }

    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
        public DbSet<SaleRecord> SaleRecords => Set<SaleRecord>();
        public DbSet<PickupTask> PickupTasks => Set<PickupTask>();
        public DbSet<PickupAssignmentEvent> PickupAssignmentEvents => Set<PickupAssignmentEvent>();
        public DbSet<SegregationBatch> SegregationBatches => Set<SegregationBatch>();
    }
}
