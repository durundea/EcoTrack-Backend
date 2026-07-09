namespace EcoTrack.Application.Recycling.Contracts;

public record GetRecyclingBatchesQueryRequest(
    int Page = 1,
    int PageSize = 20);
