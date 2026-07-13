using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Xunit;

namespace EcoTrack.UnitTests.Recycling;

public class RecyclingBatchStageTests
{
    [Fact]
    public void CreateFromSegregation_CreatesValidBatch_InSegregatedStage()
    {
        // Arrange
        var segregationBatchId = Guid.NewGuid();
        var pickupTaskId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;

        // Act
        var batch = RecyclingBatch.CreateFromSegregation(
            segregationBatchId,
            pickupTaskId,
            "plastic",
            10,
            createdByUserId,
            createdAtUtc);

        // Assert
        Assert.NotEqual(Guid.Empty, batch.Id);
        Assert.Equal(segregationBatchId, batch.SegregationBatchId);
        Assert.Equal(pickupTaskId, batch.PickupTaskId);
        Assert.Equal("plastic", batch.SourceCategory);
        Assert.Equal(10, batch.SourceWeightKg);
        Assert.Equal(RecyclingBatchStage.Segregated, batch.Stage);
        Assert.Single(batch.StageHistory);
    }

    [Fact]
    public void AdvanceStage_FromSegregatedToProcessing_Succeeds()
    {
        // Arrange
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow);

        var actorUserId = Guid.NewGuid();
        var transitionTime = DateTime.UtcNow;

        // Act
        batch.AdvanceStage(RecyclingBatchStage.Processing, actorUserId, transitionTime);

        // Assert
        Assert.Equal(RecyclingBatchStage.Processing, batch.Stage);
        Assert.Equal(actorUserId, batch.UpdatedByUserId);
        Assert.True(batch.StageHistory.Count >= 2);
    }

    [Fact]
    public void AdvanceStage_FromProcessingToConverted_Succeeds()
    {
        // Arrange
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow);

        batch.AdvanceStage(RecyclingBatchStage.Processing, Guid.NewGuid(), DateTime.UtcNow);

        var actorUserId = Guid.NewGuid();

        // Act
        batch.AdvanceStage(RecyclingBatchStage.Converted, actorUserId, DateTime.UtcNow);

        // Assert
        Assert.Equal(RecyclingBatchStage.Converted, batch.Stage);
        Assert.True(batch.StageHistory.Count >= 3);
    }

    [Fact]
    public void AdvanceStage_InvalidTransition_ThrowsInvalidOperationException()
    {
        // Arrange
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow);

        // Act & Assert - try to go backwards
        Assert.Throws<InvalidOperationException>(() =>
            batch.AdvanceStage(RecyclingBatchStage.Collected, Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void MarkInventoryUpdated_SetsFlag()
    {
        // Arrange
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow);

        Assert.False(batch.InventoryUpdated);

        // Act
        batch.MarkInventoryUpdated();

        // Assert
        Assert.True(batch.InventoryUpdated);
    }
}

public class ProductConversionTests
{
    [Fact]
    public void Create_CreatesValidConversion()
    {
        // Arrange
        var recyclingBatchId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        // Act
        var conversion = ProductConversion.Create(
            recyclingBatchId,
            "Flakes",
            8,
            "kg",
            createdAt);

        // Assert
        Assert.NotEqual(Guid.Empty, conversion.Id);
        Assert.Equal(recyclingBatchId, conversion.RecyclingBatchId);
        Assert.Equal("Flakes", conversion.ProductName);
        Assert.Equal(8, conversion.Quantity);
        Assert.Equal("kg", conversion.Unit);
        Assert.Null(conversion.SyncedAtUtc);
        Assert.Null(conversion.SyncRunId);
    }

    [Fact]
    public void MarkSynced_SetsAllSyncFields()
    {
        // Arrange
        var conversion = ProductConversion.Create(
            Guid.NewGuid(),
            "Flakes",
            8,
            "kg",
            DateTime.UtcNow);

        var syncRunId = Guid.NewGuid().ToString();
        var syncedByUserId = Guid.NewGuid();
        var syncedAtUtc = DateTime.UtcNow;

        // Act
        conversion.MarkSynced(syncRunId, syncedByUserId, syncedAtUtc);

        // Assert
        Assert.Equal(syncRunId, conversion.SyncRunId);
        Assert.Equal(syncedByUserId, conversion.SyncedByUserId);
        Assert.Equal(syncedAtUtc, conversion.SyncedAtUtc);
    }

    [Fact]
    public void Create_WithInvalidQuantity_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductConversion.Create(
                Guid.NewGuid(),
                "Flakes",
                0, // Invalid: must be > 0
                "kg",
                DateTime.UtcNow));
    }
}
