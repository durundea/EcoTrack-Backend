namespace EcoTrack.Application.Inventory.Contracts;

public sealed record DashboardAnalyticsResponse(
    DashboardRangeResponse Range,
    DashboardKpisResponse Kpis,
    IReadOnlyList<DashboardCategoryMetricResponse> WasteByCategory,
    IReadOnlyList<DashboardCategoryMetricResponse> CategoryDistribution,
    PendingSalesApprovalsResponse PendingSalesApprovals);

public sealed record DashboardRangeResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string Label);

public sealed record DashboardKpisResponse(
    decimal TotalWasteProcessedKg,
    decimal RevenueInr,
    decimal RecyclingEfficiencyPercent,
    decimal Co2ReductionKg);

public sealed record DashboardCategoryMetricResponse(
    string Category,
    decimal WeightKg,
    decimal SharePercent);

public sealed record PendingSalesApprovalsResponse(
    int Count,
    bool IsDataAvailable,
    string? Message);
