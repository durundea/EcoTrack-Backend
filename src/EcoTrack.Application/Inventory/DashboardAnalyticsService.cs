using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.Application.Inventory;

public class DashboardAnalyticsService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly DashboardAnalyticsOptions _options;

    public DashboardAnalyticsService(
        IApplicationDbContext dbContext,
        IOptions<DashboardAnalyticsOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<DashboardAnalyticsResponse> GetDashboardAsync(
        GetDashboardAnalyticsQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var (fromUtc, toUtc, label) = NormalizeRange(request.FromUtc, request.ToUtc, nowUtc);
        var wasteType = ParseWasteType(request.WasteType);

        var salesQuery = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.SoldAtUtc >= fromUtc && x.SoldAtUtc <= toUtc)
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.Approved || x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            salesQuery = salesQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var joinedSalesQuery = from sale in salesQuery
                               join item in _dbContext.InventoryItems.AsNoTracking() on sale.InventoryItemId equals item.Id
                               select new SalesRow(
                                   item.Category,
                                   sale.QuantitySold,
                                   sale.RevenueInr);

        if (wasteType.HasValue)
        {
            joinedSalesQuery = joinedSalesQuery.Where(x => x.Category == wasteType.Value);
        }

        var salesRows = await joinedSalesQuery.ToListAsync(cancellationToken);

        var totalWasteProcessedKg = salesRows.Sum(x => (decimal)x.QuantitySold);
        var revenueInr = salesRows.Sum(x => x.RevenueInr);

        var groupedByCategory = salesRows
            .GroupBy(x => x.Category)
            .Select(g => new GroupedCategoryWeight(
                g.Key.ToString(),
                g.Sum(x => (decimal)x.QuantitySold)))
            .OrderByDescending(x => x.WeightKg)
            .ToList();

        var categoryRows = BuildCategoryRows(groupedByCategory, totalWasteProcessedKg);

        var inventoryQuery = _dbContext.InventoryItems.AsNoTracking();
        if (wasteType.HasValue)
        {
            inventoryQuery = inventoryQuery.Where(x => x.Category == wasteType.Value);
        }

        var totalCollectedKg = await inventoryQuery
            .SumAsync(x => (decimal?)x.QuantityKg, cancellationToken) ?? 0m;

        var recyclingEfficiencyPercent = totalCollectedKg == 0m
            ? 0m
            : Math.Round((totalWasteProcessedKg / totalCollectedKg) * 100m, 1, MidpointRounding.AwayFromZero);

        var co2ReductionKg = Math.Round(
            salesRows.Sum(x => (decimal)x.QuantitySold * GetCo2Factor(x.Category)),
            1,
            MidpointRounding.AwayFromZero);

        var pendingApprovalsQuery = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            pendingApprovalsQuery = pendingApprovalsQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var pendingApprovalsCount = await pendingApprovalsQuery.CountAsync(cancellationToken);

        return new DashboardAnalyticsResponse(
            new DashboardRangeResponse(fromUtc, toUtc, label),
            new DashboardKpisResponse(totalWasteProcessedKg, revenueInr, recyclingEfficiencyPercent, co2ReductionKg),
            categoryRows,
            categoryRows,
            new PendingSalesApprovalsResponse(pendingApprovalsCount, true, null));
    }

    private static (DateTime FromUtc, DateTime ToUtc, string Label) NormalizeRange(
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime nowUtc)
    {
        if (!fromUtc.HasValue && !toUtc.HasValue)
        {
            return (nowUtc.AddDays(-30), nowUtc, "Last 30 days");
        }

        if (fromUtc.HasValue && !toUtc.HasValue)
        {
            return (fromUtc.Value, fromUtc.Value.AddDays(30), "Custom range");
        }

        if (!fromUtc.HasValue && toUtc.HasValue)
        {
            return (toUtc.Value.AddDays(-30), toUtc.Value, "Custom range");
        }

        if (fromUtc!.Value > toUtc!.Value)
        {
            throw new BadRequestException("FromUtc must be less than or equal to ToUtc.");
        }

        return (fromUtc.Value, toUtc.Value, "Custom range");
    }

    private static InventoryCategory? ParseWasteType(string? wasteType)
    {
        if (string.IsNullOrWhiteSpace(wasteType) || string.Equals(wasteType, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(wasteType, "rawWaste", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryCategory.RawWaste;
        }

        if (string.Equals(wasteType, "recycledProduct", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryCategory.RecycledProduct;
        }

        throw new BadRequestException("WasteType must be one of: all, rawWaste, recycledProduct.");
    }

    private decimal GetCo2Factor(InventoryCategory category)
    {
        var categoryKey = category.ToString();
        return _options.Co2FactorsKgPerKgByCategory.TryGetValue(categoryKey, out var factor)
            ? factor
            : 0m;
    }

    private static IReadOnlyList<DashboardCategoryMetricResponse> BuildCategoryRows(
        IReadOnlyList<GroupedCategoryWeight> groupedRows,
        decimal totalWeightKg)
    {
        if (totalWeightKg == 0m)
        {
            return Array.Empty<DashboardCategoryMetricResponse>();
        }

        return groupedRows
            .Select(x => new DashboardCategoryMetricResponse(
                x.Category,
                x.WeightKg,
                Math.Round((x.WeightKg / totalWeightKg) * 100m, 1, MidpointRounding.AwayFromZero)))
            .ToList();
    }

    private sealed record SalesRow(InventoryCategory Category, int QuantitySold, decimal RevenueInr);

    private sealed record GroupedCategoryWeight(string Category, decimal WeightKg);
}
