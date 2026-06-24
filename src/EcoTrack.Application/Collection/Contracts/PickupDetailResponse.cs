namespace EcoTrack.Application.Collection.Contracts;

public sealed record PickupDetailResponse(
    Guid Id,
    string PickupCode,
    string SiteName,
    string SiteAddressText,
    DateTime ScheduledAtUtc,
    decimal EstimatedWeightKg,
    decimal? CollectedWeightKg,
    string Status,
    Guid? AssignedCollectorUserId,
    string? AssignedCollectorDisplayName,
    string? Notes,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? CancelledByUserId,
    DateTime? CancelledAtUtc,
    string? CancelReason,
    IReadOnlyList<AssignmentEventResponse> AssignmentEvents);