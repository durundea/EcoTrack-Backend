using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class RecyclingBatch : Entity
{
    private List<RecyclingBatchStageHistoryEntry> _stageHistory = new();

    private RecyclingBatch() { }

    public Guid SegregationBatchId { get; private set; }
    public Guid PickupTaskId { get; private set; }
    public string SourceCategory { get; private set; } = null!; // plastic, organic, metal, paper, ewaste
    public decimal SourceWeightKg { get; private set; }
    public RecyclingBatchStage Stage { get; private set; }
    public string OutputProduct { get; private set; } = string.Empty;
    public decimal OutputQuantity { get; private set; }
    public bool InventoryUpdated { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public IReadOnlyList<RecyclingBatchStageHistoryEntry> StageHistory => _stageHistory.AsReadOnly();
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static RecyclingBatch CreateFromSegregation(
        Guid segregationBatchId,
        Guid pickupTaskId,
        string sourceCategory,
        decimal sourceWeightKg,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (segregationBatchId == Guid.Empty) throw new ArgumentException("SegregationBatchId is required.", nameof(segregationBatchId));
        if (pickupTaskId == Guid.Empty) throw new ArgumentException("PickupTaskId is required.", nameof(pickupTaskId));
        if (string.IsNullOrWhiteSpace(sourceCategory)) throw new ArgumentException("SourceCategory is required.", nameof(sourceCategory));
        if (sourceWeightKg <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWeightKg), "SourceWeightKg must be greater than 0.");
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));

        var batch = new RecyclingBatch
        {
            Id = Guid.NewGuid(),
            SegregationBatchId = segregationBatchId,
            PickupTaskId = pickupTaskId,
            SourceCategory = sourceCategory,
            SourceWeightKg = sourceWeightKg,
            Stage = RecyclingBatchStage.Segregated,
            OutputProduct = string.Empty,
            OutputQuantity = 0,
            InventoryUpdated = false,
            CreatedByUserId = createdByUserId,
            UpdatedByUserId = null,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        batch._stageHistory.Add(new RecyclingBatchStageHistoryEntry(
            batch.Stage,
            createdAtUtc,
            createdByUserId));

        return batch;
    }

    public void AdvanceStage(RecyclingBatchStage newStage, Guid actorUserId, DateTime transitionAtUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        // Only allow forward transitions: Segregated -> Processing -> Converted
        var validNextStages = Stage switch
        {
            RecyclingBatchStage.Segregated => new[] { RecyclingBatchStage.Processing },
            RecyclingBatchStage.Processing => new[] { RecyclingBatchStage.Converted },
            _ => Array.Empty<RecyclingBatchStage>()
        };

        if (!validNextStages.Contains(newStage))
        {
            throw new InvalidOperationException(
                $"Cannot transition from {Stage} to {newStage}. Valid transitions from {Stage}: {string.Join(", ", validNextStages)}");
        }

        Stage = newStage;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = transitionAtUtc;

        _stageHistory.Add(new RecyclingBatchStageHistoryEntry(
            newStage,
            transitionAtUtc,
            actorUserId));
    }

    public void MarkInventoryUpdated()
    {
        InventoryUpdated = true;
    }
}

public class RecyclingBatchStageHistoryEntry
{
    public RecyclingBatchStageHistoryEntry() { }

    public RecyclingBatchStageHistoryEntry(RecyclingBatchStage stage, DateTime atUtc, Guid byUserId)
    {
        Stage = stage;
        AtUtc = atUtc;
        ByUserId = byUserId;
    }

    public RecyclingBatchStage Stage { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid ByUserId { get; set; }
}
