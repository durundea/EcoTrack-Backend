namespace EcoTrack.Application.Collection.Contracts;

public sealed record CreatePickupRequest(
    string SiteName,
    string SiteAddressText,
    DateTime ScheduledAtUtc,
    decimal EstimatedWeightKg,
    string? Notes);