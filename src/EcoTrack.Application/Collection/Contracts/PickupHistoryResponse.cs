namespace EcoTrack.Application.Collection.Contracts;

public sealed record PickupHistoryResponse(IReadOnlyList<AssignmentEventResponse> Events);