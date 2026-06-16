namespace EcoTrack.Application.Inventory;

public sealed class DashboardAnalyticsOptions
{
    public const string SectionName = "DashboardAnalytics";

    public Dictionary<string, decimal> Co2FactorsKgPerKgByCategory { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
