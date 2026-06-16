namespace EcoTrack.Application.Inventory.Contracts;

public sealed record GetDashboardAnalyticsQueryRequest(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? WasteType);
