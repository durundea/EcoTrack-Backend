namespace EcoTrack.Application.Collection.Contracts;

public sealed record UpdatePickupRequest(
    string? SiteName,
    string? SiteAddressText,
    DateTime? ScheduledAtUtc,
    decimal? EstimatedWeightKg,
    string? Notes);