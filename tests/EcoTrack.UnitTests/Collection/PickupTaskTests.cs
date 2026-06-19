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

        Action act = () => pickup.SendToSegregation(now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateByAdmin_WithEmptySiteName_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");

        Action act = () => pickup.UpdateByAdmin(" ", "Updated Address", now.AddDays(2), 150m, null, now.AddMinutes(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateByAdmin_WithEmptySiteAddress_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");

        Action act = () => pickup.UpdateByAdmin("Updated Site", "", now.AddDays(2), 150m, null, now.AddMinutes(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignCollector_WithEmptyCollectorId_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");

        Action act = () => pickup.AssignCollector(Guid.Empty, Guid.NewGuid(), now.AddMinutes(5), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignCollector_WithEmptyAdminId_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");

        Action act = () => pickup.AssignCollector(Guid.NewGuid(), Guid.Empty, now.AddMinutes(5), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignCollector_RecordsEventFields_PreviousNewAndChangedBy()
    {
        var now = DateTime.UtcNow;
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", now.AddDays(1), 120m, null, Guid.NewGuid(), now, "P-1001");
        var firstAdminId = Guid.NewGuid();
        var secondAdminId = Guid.NewGuid();
        var firstCollectorId = Guid.NewGuid();
        var secondCollectorId = Guid.NewGuid();

        pickup.AssignCollector(firstCollectorId, firstAdminId, now.AddMinutes(5), "first assign");
        pickup.AssignCollector(secondCollectorId, secondAdminId, now.AddMinutes(10), "reassign");

        pickup.AssignmentEvents.Should().HaveCount(2);

        var firstEvent = pickup.AssignmentEvents.ElementAt(0);
        firstEvent.PreviousCollectorUserId.Should().BeNull();
        firstEvent.NewCollectorUserId.Should().Be(firstCollectorId);
        firstEvent.ChangedByUserId.Should().Be(firstAdminId);

        var secondEvent = pickup.AssignmentEvents.ElementAt(1);
        secondEvent.PreviousCollectorUserId.Should().Be(firstCollectorId);
        secondEvent.NewCollectorUserId.Should().Be(secondCollectorId);
        secondEvent.ChangedByUserId.Should().Be(secondAdminId);
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
