using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Inventory;

public class SaleRecordTests
{
    private static readonly DateTime FixedTime = new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubmitForApproval_WhenStatusIsDraft_ChangesStatusToPendingApproval()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 2, 120m, FixedTime, FixedTime);

        sale.SubmitForApproval(requestedById, UserRole.Collector, FixedTime);

        sale.ApprovalStatus.Should().Be(SaleApprovalStatus.PendingApproval);
        sale.UpdatedAtUtc.Should().Be(FixedTime);
    }

    [Fact]
    public void Approve_WhenStatusIsPendingApproval_StoresApproverAndLocksRecord()
    {
        var approverId = Guid.NewGuid();
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 2, 120m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Collector, FixedTime);

        sale.Approve(approverId, UserRole.Admin, FixedTime);

        sale.ApprovalStatus.Should().Be(SaleApprovalStatus.Approved);
        sale.ApprovedByUserId.Should().Be(approverId);
        sale.ApprovedAtUtc.Should().Be(FixedTime);
        sale.CanBeModified.Should().BeFalse();
    }

    [Fact]
    public void CreateDraft_WithZeroQuantity_ThrowsArgumentOutOfRangeException()
    {
        var action = () => SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 0, 120m, FixedTime, FixedTime);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SubmitForApproval_WhenAlreadyPending_ThrowsInvalidOperationException()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);

        var action = () => sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Approve_WhenRoleIsCollector_ThrowsInvalidOperationException()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);

        var action = () => sale.Approve(Guid.NewGuid(), UserRole.Collector, FixedTime);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SubmitForApproval_WhenCollectorIsNotOwner_ThrowsInvalidOperationException()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), ownerId, 1, 60m, FixedTime, FixedTime);

        var action = () => sale.SubmitForApproval(otherId, UserRole.Collector, FixedTime);
        action.Should().Throw<InvalidOperationException>().WithMessage("*own*");
    }

    [Fact]
    public void Reject_WhenRoleIsAdmin_StoresRejectorAndReason()
    {
        var rejectorId = Guid.NewGuid();
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);

        sale.Reject(rejectorId, UserRole.Admin, "Incorrect quantity", FixedTime);

        sale.ApprovalStatus.Should().Be(SaleApprovalStatus.Rejected);
        sale.RejectedByUserId.Should().Be(rejectorId);
        sale.RejectionReason.Should().Be("Incorrect quantity");
        sale.RejectedAtUtc.Should().Be(FixedTime);
        sale.ApprovedByUserId.Should().BeNull();
    }

    [Fact]
    public void Reject_WhenRoleIsCollector_ThrowsInvalidOperationException()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);

        var action = () => sale.Reject(Guid.NewGuid(), UserRole.Collector, "reason", FixedTime);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_WhenNotPending_ThrowsInvalidOperationException()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);

        var action = () => sale.Reject(Guid.NewGuid(), UserRole.Admin, "reason", FixedTime);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureCanBeModified_WhenApproved_ThrowsInvalidOperationException()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);
        sale.Approve(Guid.NewGuid(), UserRole.Admin, FixedTime);

        var action = () => sale.EnsureCanBeModified();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureCanBeModified_WhenDraft_DoesNotThrow()
    {
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 1, 60m, FixedTime, FixedTime);

        var action = () => sale.EnsureCanBeModified();
        action.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanBeModified_WhenRejected_DoesNotThrow()
    {
        var requestedById = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), requestedById, 1, 60m, FixedTime, FixedTime);
        sale.SubmitForApproval(requestedById, UserRole.Admin, FixedTime);
        sale.Reject(Guid.NewGuid(), UserRole.Admin, "Wrong amount", FixedTime);

        var action = () => sale.EnsureCanBeModified();
        action.Should().NotThrow();
    }
}
