namespace EcoTrack.Application.Recycling.Contracts;

public record ProductConversionResponse(
    Guid Id,
    Guid RecyclingBatchId,
    string ProductName,
    decimal Quantity,
    string Unit,
    DateTime? SyncedAtUtc,
    string? SyncRunId,
    Guid? SyncedByUserId,
    DateTime CreatedAt);
