namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchDetailResponse(
    Guid Id,
    Guid SegregationBatchId,
    Guid PickupTaskId,
    string SourceCategory,
    decimal SourceWeightKg,
    string Stage,
    string OutputProduct,
    decimal OutputQuantity,
    bool InventoryUpdated,
    Guid CreatedByUserId,
    Guid? UpdatedByUserId,
    List<RecyclingBatchStageHistoryEntryResponse> StageHistory,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
