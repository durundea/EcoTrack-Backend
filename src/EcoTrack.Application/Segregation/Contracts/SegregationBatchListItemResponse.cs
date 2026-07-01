namespace EcoTrack.Application.Segregation.Contracts;

public sealed record SegregationBatchListItemResponse(
    Guid Id,
    Guid PickupTaskId,
    string BatchCode,
    string PickupCode,
    string Status,
    DateTime? RecordedAtUtc,
    DateTime? RecycledAtUtc);
