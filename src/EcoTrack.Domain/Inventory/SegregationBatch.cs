using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class SegregationBatch : Entity
{
    private SegregationBatch() { }

    public Guid PickupTaskId { get; private set; }
    public string BatchCode { get; private set; } = null!;
    public SegregationBatchStatus Status { get; private set; }
    public decimal? PlasticKg { get; private set; }
    public decimal? OrganicKg { get; private set; }
    public decimal? MetalKg { get; private set; }
    public decimal? PaperKg { get; private set; }
    public decimal? EWasteKg { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public DateTime? RecordedAtUtc { get; private set; }
    public Guid? RecycledByUserId { get; private set; }
    public DateTime? RecycledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SegregationBatch CreatePending(Guid pickupTaskId, string batchCode, DateTime createdAtUtc)
    {
        if (pickupTaskId == Guid.Empty) throw new ArgumentException("PickupTaskId is required.", nameof(pickupTaskId));
        if (string.IsNullOrWhiteSpace(batchCode)) throw new ArgumentException("BatchCode is required.", nameof(batchCode));

        return new SegregationBatch
        {
            Id = Guid.NewGuid(),
            PickupTaskId = pickupTaskId,
            BatchCode = batchCode,
            Status = SegregationBatchStatus.Pending,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public void Record(decimal plasticKg, decimal organicKg, decimal metalKg, decimal paperKg, decimal eWasteKg, Guid actorUserId, DateTime recordedAtUtc)
    {
        if (Status != SegregationBatchStatus.Pending)
            throw new InvalidOperationException($"Cannot record segregation data on batch in {Status} status.");
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        ValidateWeight(plasticKg, nameof(plasticKg));
        ValidateWeight(organicKg, nameof(organicKg));
        ValidateWeight(metalKg, nameof(metalKg));
        ValidateWeight(paperKg, nameof(paperKg));
        ValidateWeight(eWasteKg, nameof(eWasteKg));

        if (plasticKg + organicKg + metalKg + paperKg + eWasteKg <= 0m)
            throw new ArgumentException("At least one waste category must be greater than zero.");

        PlasticKg = plasticKg;
        OrganicKg = organicKg;
        MetalKg = metalKg;
        PaperKg = paperKg;
        EWasteKg = eWasteKg;
        RecordedByUserId = actorUserId;
        RecordedAtUtc = recordedAtUtc;
        Status = SegregationBatchStatus.Recorded;
        UpdatedAtUtc = recordedAtUtc;
    }

    public void MarkRecycled(Guid actorUserId, DateTime recycledAtUtc)
    {
        if (Status != SegregationBatchStatus.Recorded)
            throw new InvalidOperationException($"Cannot mark recycled on batch in {Status} status.");
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        RecycledByUserId = actorUserId;
        RecycledAtUtc = recycledAtUtc;
        Status = SegregationBatchStatus.Recycled;
        UpdatedAtUtc = recycledAtUtc;
    }

    private static void ValidateWeight(decimal value, string paramName)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(paramName, "Weight must be greater than or equal to zero.");
    }
}
