namespace EcoTrack.Application.Collection.Contracts;

public sealed record GetPickupsQueryRequest(
    string? Status,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = "scheduledAtUtc",
    string? SortDirection = "desc");