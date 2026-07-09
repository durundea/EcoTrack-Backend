using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class ProductConversion : Entity
{
    private ProductConversion() { }

    public Guid RecyclingBatchId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = null!; // kg or units
    public DateTime? SyncedAtUtc { get; private set; }
    public string? SyncRunId { get; private set; }
    public Guid? SyncedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static ProductConversion Create(
        Guid recyclingBatchId,
        string productName,
        decimal quantity,
        string unit,
        DateTime createdAt)
    {
        if (recyclingBatchId == Guid.Empty) throw new ArgumentException("RecyclingBatchId is required.", nameof(recyclingBatchId));
        if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("ProductName is required.", nameof(productName));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));

        return new ProductConversion
        {
            Id = Guid.NewGuid(),
            RecyclingBatchId = recyclingBatchId,
            ProductName = productName.Trim(),
            Quantity = quantity,
            Unit = unit,
            SyncedAtUtc = null,
            SyncRunId = null,
            SyncedByUserId = null,
            CreatedAt = createdAt
        };
    }

    public void MarkSynced(string syncRunId, Guid syncedByUserId, DateTime syncedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(syncRunId)) throw new ArgumentException("SyncRunId is required.", nameof(syncRunId));
        if (syncedByUserId == Guid.Empty) throw new ArgumentException("SyncedByUserId is required.", nameof(syncedByUserId));

        SyncRunId = syncRunId;
        SyncedByUserId = syncedByUserId;
        SyncedAtUtc = syncedAtUtc;
    }
}
