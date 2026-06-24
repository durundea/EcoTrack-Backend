namespace EcoTrack.Application.Collection.Contracts;

public sealed record AssignPickupRequest(Guid AssignedCollectorUserId, string? Note);