using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Segregation;

public class SegregationBatchTests
{
    [Fact]
    public void Record_FromPending_WithValidWeights_MovesToRecorded()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);
        var actorId = Guid.NewGuid();

        batch.Record(50m, 30m, 20m, 15m, 5m, actorId, now.AddMinutes(10));

        batch.Status.Should().Be(SegregationBatchStatus.Recorded);
        batch.RecordedByUserId.Should().Be(actorId);
        batch.RecordedAtUtc.Should().NotBeNull();
        batch.PlasticKg.Should().Be(50m);
        batch.OrganicKg.Should().Be(30m);
        batch.MetalKg.Should().Be(20m);
        batch.PaperKg.Should().Be(15m);
        batch.EWasteKg.Should().Be(5m);
    }

    [Fact]
    public void Record_WithAllZeroWeights_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);

        Action act = () => batch.Record(0m, 0m, 0m, 0m, 0m, Guid.NewGuid(), now.AddMinutes(5));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_WithNegativeWeight_ThrowsArgumentOutOfRangeException()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);

        Action act = () => batch.Record(-1m, 0m, 0m, 1m, 0m, Guid.NewGuid(), now.AddMinutes(5));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkRecycled_FromRecorded_MovesToRecycled()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);
        var recorderId = Guid.NewGuid();
        var recyclerId = Guid.NewGuid();

        batch.Record(10m, 0m, 0m, 0m, 0m, recorderId, now.AddMinutes(10));
        batch.MarkRecycled(recyclerId, now.AddMinutes(20));

        batch.Status.Should().Be(SegregationBatchStatus.Recycled);
        batch.RecycledByUserId.Should().Be(recyclerId);
        batch.RecycledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarkRecycled_FromPending_ThrowsInvalidOperationException()
    {
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", DateTime.UtcNow);

        Action act = () => batch.MarkRecycled(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5));

        act.Should().Throw<InvalidOperationException>();
    }
}
