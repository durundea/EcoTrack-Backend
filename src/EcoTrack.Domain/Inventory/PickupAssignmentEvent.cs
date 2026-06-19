using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class PickupAssignmentEvent : Entity
{
    private PickupAssignmentEvent() { }

    public Guid PickupTaskId { get; private set; }
    public Guid? PreviousCollectorUserId { get; private set; }
    public Guid NewCollectorUserId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public string? Note { get; private set; }

    public static PickupAssignmentEvent Create(
        Guid pickupTaskId,
        Guid? previousCollectorUserId,
        Guid newCollectorUserId,
        Guid changedByUserId,
        DateTime changedAtUtc,
        string? note)
    {
        if (pickupTaskId == Guid.Empty) throw new ArgumentException("PickupTaskId is required.", nameof(pickupTaskId));
        if (newCollectorUserId == Guid.Empty) throw new ArgumentException("NewCollectorUserId is required.", nameof(newCollectorUserId));
        if (changedByUserId == Guid.Empty) throw new ArgumentException("ChangedByUserId is required.", nameof(changedByUserId));

        return new PickupAssignmentEvent
        {
            Id = Guid.NewGuid(),
            PickupTaskId = pickupTaskId,
            PreviousCollectorUserId = previousCollectorUserId,
            NewCollectorUserId = newCollectorUserId,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc,
            Note = note,
        };
    }
}
