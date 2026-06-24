namespace EcoTrack.Application.Collection.Contracts;

public sealed record PickupListItemResponse(
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
    string? Notes);