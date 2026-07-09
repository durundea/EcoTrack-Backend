namespace EcoTrack.Application.Recycling.Contracts;

public record InventorySyncResponse(
    int UpdatedItemsCount,
    int CreatedItemsCount,
    int SkippedCount,
    string SyncRunId);
