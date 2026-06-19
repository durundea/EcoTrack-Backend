namespace EcoTrack.Domain.Inventory;

public enum PickupStatus
{
    Scheduled = 1,
    Assigned = 2,
    Collected = 3,
    SentToSegregation = 4,
    Cancelled = 5,
}
