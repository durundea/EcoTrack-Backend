namespace EcoTrack.Application.Segregation.Contracts;

public sealed record SegregationBatchDetailResponse(
    Guid Id,
    string BatchCode,
    string Status,
    Guid PickupTaskId,
    string PickupCode,
    string SiteName,
    string SiteAddressText,
    DateTime ScheduledAtUtc,
    decimal CollectedWeightKg,
    decimal? PlasticKg,
    decimal? OrganicKg,
    decimal? MetalKg,
    decimal? PaperKg,
    decimal? EWasteKg,
    Guid? RecordedByUserId,
    DateTime? RecordedAtUtc,
    Guid? RecycledByUserId,
    DateTime? RecycledAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
