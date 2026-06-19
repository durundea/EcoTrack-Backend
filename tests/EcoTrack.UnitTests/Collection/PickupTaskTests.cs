using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Collection;

public class PickupTaskTests
{
    [Fact]
    public void Assign_FromScheduled_MovesToAssigned_AndAddsEvent()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, "note", Guid.NewGuid(), now, "P-1001");

        var adminId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();

        pickup.AssignCollector(collectorId, adminId, now.AddMinutes(5), "initial");

        pickup.Status.Should().Be(PickupStatus.Assigned);
        pickup.AssignmentEvents.Should().HaveCount(1);
        pickup.AssignedCollectorUserId.Should().Be(collectorId);
    }

    [Fact]
    public void Cancel_AfterCollected_ThrowsInvalidOperationException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");
        var adminId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();

        pickup.AssignCollector(collectorId, adminId, now.AddMinutes(5), null);
        pickup.MarkCollected(115m, collectorId, now.AddHours(1));

        Action act = () => pickup.Cancel(adminId, now.AddHours(2), "late cancel");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SendToSegregation_WithoutCollectedStatus_ThrowsInvalidOperationException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");

        Action act = () => pickup.SendToSegregation(Guid.NewGuid(), now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateScheduled_WithNonPositiveEstimatedWeight_ThrowsArgumentOutOfRangeException()
    {
        var now = DateTime.UtcNow;

        Action act = () => PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 0m, null, Guid.NewGuid(), now, "P-1001");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkCollected_WithNonPositiveWeight_ThrowsArgumentOutOfRangeException()
    {
        var now = DateTime.UtcNow;
        var adminId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");
        pickup.AssignCollector(collectorId, adminId, now.AddMinutes(5), null);

        Action act = () => pickup.MarkCollected(0m, collectorId, now.AddHours(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
