namespace EcoTrack.Application.Segregation.Contracts;

public sealed record GetSegregationBatchesQueryRequest(
    string? Status,
    int Page = 1,
    int PageSize = 20);
