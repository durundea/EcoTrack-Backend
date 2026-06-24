namespace EcoTrack.Application.Collection.Contracts;

public sealed record AssignmentEventResponse(
    Guid Id,
    Guid PickupTaskId,
    Guid? PreviousCollectorUserId,
    Guid NewCollectorUserId,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc,
    string? Note);