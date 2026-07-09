namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchStageHistoryEntryResponse(
    string Stage,
    DateTime AtUtc,
    Guid ByUserId);
