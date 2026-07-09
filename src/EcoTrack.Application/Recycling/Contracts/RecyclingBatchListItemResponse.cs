namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchListItemResponse(
    Guid Id,
    Guid SegregationBatchId,
    Guid PickupTaskId,
    string SourceCategory,
    decimal SourceWeightKg,
    string Stage,
    string OutputProduct,
    decimal OutputQuantity,
    bool InventoryUpdated);
