using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class PickupTask : Entity
{
    private readonly List<PickupAssignmentEvent> _assignmentEvents = new();

    private PickupTask() { }

    public string PickupCode { get; private set; } = null!;
    public string SiteName { get; private set; } = null!;
    public string SiteAddressText { get; private set; } = null!;
    public DateTime ScheduledAtUtc { get; private set; }
    public decimal EstimatedWeightKg { get; private set; }
    public decimal? CollectedWeightKg { get; private set; }
    public PickupStatus Status { get; private set; }
    public Guid? AssignedCollectorUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<PickupAssignmentEvent> AssignmentEvents => _assignmentEvents;

    public static PickupTask CreateScheduled(
        string siteName,
        string siteAddressText,
        DateTime scheduledAtUtc,
        decimal estimatedWeightKg,
        string? notes,
        Guid createdByUserId,
        DateTime createdAtUtc,
        string pickupCode)
    {
        if (string.IsNullOrWhiteSpace(siteName)) throw new ArgumentException("SiteName is required.", nameof(siteName));
        if (string.IsNullOrWhiteSpace(siteAddressText)) throw new ArgumentException("SiteAddressText is required.", nameof(siteAddressText));
        if (estimatedWeightKg <= 0m) throw new ArgumentOutOfRangeException(nameof(estimatedWeightKg), "EstimatedWeightKg must be greater than zero.");

        return new PickupTask
        {
            Id = Guid.NewGuid(),
            PickupCode = pickupCode,
            SiteName = siteName,
            SiteAddressText = siteAddressText,
            ScheduledAtUtc = scheduledAtUtc,
            EstimatedWeightKg = estimatedWeightKg,
            Notes = notes,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            Status = PickupStatus.Scheduled,
        };
    }

    public void AssignCollector(Guid newCollectorUserId, Guid changedByUserId, DateTime changedAtUtc, string? note)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation or PickupStatus.Collected)
            throw new InvalidOperationException("Pickup cannot be assigned in current status.");

        var previousCollectorUserId = AssignedCollectorUserId;

        AssignedCollectorUserId = newCollectorUserId;
        AssignedAtUtc = changedAtUtc;
        Status = PickupStatus.Assigned;
        UpdatedAtUtc = changedAtUtc;

        _assignmentEvents.Add(PickupAssignmentEvent.Create(Id, previousCollectorUserId, newCollectorUserId, changedByUserId, changedAtUtc, note));
    }

    public void MarkCollected(decimal collectedWeightKg, Guid actorUserId, DateTime collectedAtUtc)
    {
        if (Status != PickupStatus.Assigned) throw new InvalidOperationException("Only assigned pickups can be collected.");
        if (AssignedCollectorUserId.HasValue && AssignedCollectorUserId.Value != actorUserId)
            throw new InvalidOperationException("Only assigned collector can mark pickup as collected.");
        if (collectedWeightKg <= 0m) throw new ArgumentOutOfRangeException(nameof(collectedWeightKg), "CollectedWeightKg must be greater than zero.");

        CollectedWeightKg = collectedWeightKg;
        Status = PickupStatus.Collected;
        UpdatedAtUtc = collectedAtUtc;
    }

    public void SendToSegregation(Guid actorUserId, DateTime movedAtUtc)
    {
        if (Status != PickupStatus.Collected) throw new InvalidOperationException("Only collected pickups can be sent to segregation.");

        Status = PickupStatus.SentToSegregation;
        UpdatedAtUtc = movedAtUtc;
    }

    public void Cancel(Guid cancelledByUserId, DateTime cancelledAtUtc, string? reason)
    {
        if (Status != PickupStatus.Scheduled && Status != PickupStatus.Assigned)
            throw new InvalidOperationException("Only scheduled or assigned pickups can be cancelled.");

        Status = PickupStatus.Cancelled;
        CancelledByUserId = cancelledByUserId;
        CancelledAtUtc = cancelledAtUtc;
        CancelReason = reason;
        UpdatedAtUtc = cancelledAtUtc;
    }

    public void UpdateByAdmin(
        string siteName,
        string siteAddressText,
        DateTime scheduledAtUtc,
        decimal estimatedWeightKg,
        string? notes,
        DateTime updatedAtUtc)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation)
            throw new InvalidOperationException("Terminal pickups cannot be edited.");
        if (estimatedWeightKg <= 0m)
            throw new ArgumentOutOfRangeException(nameof(estimatedWeightKg), "EstimatedWeightKg must be greater than zero.");

        SiteName = siteName;
        SiteAddressText = siteAddressText;
        ScheduledAtUtc = scheduledAtUtc;
        EstimatedWeightKg = estimatedWeightKg;
        Notes = notes;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void UpdateNotes(string? notes, DateTime updatedAtUtc)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation)
            throw new InvalidOperationException("Terminal pickups cannot be edited.");

        Notes = notes;
        UpdatedAtUtc = updatedAtUtc;
    }
}
